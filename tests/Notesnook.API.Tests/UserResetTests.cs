using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Controllers;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Jobs;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Quartz;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Tests;

public class UserResetTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private sealed class HubCaller(string userId) : HubCallerContext
    {
        public override string ConnectionId => "connection";
        public override string? UserIdentifier => userId;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim("sub", userId),
            new("scope", "notesnook.sync"), new("aud", "notesnook"), new("role", "notesnook"), new("verified", "true")], "Test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public bool Aborted { get; private set; }
        public override void Abort() => Aborted = true;
    }

    private static IJobExecutionContext Context => Stub<IJobExecutionContext>.Create(("get_CancellationToken", (_) => CancellationToken.None));

    private UserService Users()
    {
        var scope = db.CreateScope();
        return new UserService(Stub<IHttpContextAccessor>.Create(), scope.Repositories,
            Stub<IS3Service>.Create(), scope.ResetPurge, new WampServiceAccessor(Servers.NotesnookAPI), NullLogger<UserService>.Instance);
    }

    private Task PurgeAsync(ResetPurgeService? purge = null)
    {
        var scope = db.CreateScope();
        var accessor = new WampServiceAccessor(Servers.NotesnookAPI) { UserAccountService = Stub<IUserAccountService>.Create() };
        return new UserPurgeJob(scope.Repositories, scope.Devices, purge ?? scope.ResetPurge, Stub<IS3Service>.Create(), accessor, NullLogger<UserPurgeJob>.Instance).Execute(Context);
    }

    // a purge whose sync item step runs the given action instead of deleting
    private ResetPurgeService PurgeWithSyncItems(Func<IReadOnlyList<SyncItemCollection>> collections)
    {
        var scope = db.CreateScope();
        var real = scope.Repositories;
        var repositories = Stub<ISyncItemsRepositoryAccessor>.Create(
            ("get_UsersSettings", (_) => real.UsersSettings),
            ("get_InboxApiKey", (_) => real.InboxApiKey),
            ("get_InboxItems", (_) => real.InboxItems),
            ("get_Monographs", (_) => real.Monographs),
            ("get_SyncItemCollections", (_) => collections()));
        return new ResetPurgeService(repositories, scope.Devices);
    }

    private async Task<string> CreateUserAsync()
    {
        var userId = Guid.NewGuid().ToString("N");
        // without Generation, like settings stored before generations existed
        await db.Collection<BsonDocument>("user_settings").InsertOneAsync(new BsonDocument { { "UserId", userId }, { "Salt", "salt" } });
        return userId;
    }

    private SyncV2Hub Hub(TestDatabase.Scope scope, HubCallerContext caller) =>
        new(scope.Repositories, scope.Unit, scope.Devices, NullLogger<SyncV2Hub>.Instance) { Context = caller };

    private async Task PushAsync(string userId, params string[] itemIds)
    {
        Assert.Equal(1, await Hub(db.CreateScope(), new HubCaller(userId)).PushItems("device", Notes(itemIds)));
    }

    private static SyncTransferItemV2 Notes(params string[] itemIds) =>
        new() { Type = "note", Items = [.. itemIds.Select(TestItems.Note)], Count = itemIds.Length };

    private sealed record Fetched(List<string> Items, List<string> Monographs, List<string> InboxItems);

    // a new device fetches everything
    private async Task<Fetched> FetchAsync(string userId, string? deviceId = null)
    {
        var fetched = new Fetched([], [], []);
        var client = Stub<ISyncV2HubClient>.Create(
            ("SendItems", (args) => { fetched.Items.AddRange(((SyncTransferItemV2)args[0]!).Items.Select((i) => i.ItemId!)); return Task.FromResult(true); }),
            ("SendMonographs", (args) => { fetched.Monographs.AddRange(((IEnumerable<MonographMetadata>)args[0]!).Select((m) => m.ItemId!)); return Task.FromResult(true); }),
            ("SendInboxItems", (args) => { fetched.InboxItems.AddRange(((IEnumerable<InboxSyncItem>)args[0]!).Select((i) => i.ItemId!)); return Task.FromResult(true); }));
        var hub = Hub(db.CreateScope(), new HubCaller(userId));
        hub.Clients = Stub<IHubCallerClients<ISyncV2HubClient>>.Create(("get_Caller", (_) => client));
        await hub.RequestFetchV4(deviceId ?? Guid.NewGuid().ToString("N"));
        return fetched;
    }

    // what a reset that crashed before its inline purge leaves behind
    private async Task<(Monograph Monograph, InboxApiKey Key)> LeaveOldDataAsync(string userId)
    {
        var repositories = db.CreateScope().Repositories;
        var monograph = new Monograph { UserId = userId, ItemId = "monograph", Slug = Guid.NewGuid().ToString("N"), Title = "old" };
        var key = new InboxApiKey { UserId = userId, Name = "old" };
        await repositories.Monographs.Collection.InsertOneAsync(monograph);
        await repositories.InboxApiKey.Collection.InsertOneAsync(key);
        await repositories.InboxItems.Collection.InsertOneAsync(new InboxSyncItem { UserId = userId, ItemId = "inbox", Cipher = "cipher", Algorithm = Algorithms.Default, Version = 1 });
        await repositories.SyncDevices.Collection.InsertOneAsync(new SyncDevice { UserId = userId, DeviceId = "old-device", LastAccessTime = 0, IsSyncReset = false });
        return (monograph, key);
    }

    private Task<List<string>> StoredNotesAsync(string userId) =>
        db.CreateScope().Repositories.Notes.Collection.Find((i) => i.UserId == userId).Project((i) => i.ItemId!).ToListAsync();

    private Task<UserSettings> SettingsAsync(string userId) =>
        db.CreateScope().Repositories.UsersSettings.Collection.Find((s) => s.UserId == userId).FirstAsync();

    [Fact]
    public async Task A_reset_hides_old_data_straight_away()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "old");
        Assert.NotNull(await Users().GetEncryptionVerifier(userId));

        await Users().ResetUserAsync(userId, removeAttachments: false);

        Assert.Empty((await FetchAsync(userId)).Items);
        Assert.Null(await Users().GetEncryptionVerifier(userId));
        Assert.Equal(["old"], await StoredNotesAsync(userId));
    }

    [Fact]
    public async Task Data_a_crashed_reset_left_behind_is_hidden_and_purged()
    {
        var userId = await CreateUserAsync();
        await Users().ResetUserAsync(userId, removeAttachments: false);
        var (monograph, _) = await LeaveOldDataAsync(userId);

        var fetched = await FetchAsync(userId);
        var scope = db.CreateScope();
        var monographs = new MonographsController(scope.Repositories.Monographs, scope.Repositories.Generations, Stub<IURLAnalyzer>.Create(),
            scope.Devices, new WampServiceAccessor(Servers.NotesnookAPI), NullLogger<MonographsController>.Instance);

        Assert.Empty(fetched.Monographs);
        Assert.Empty(fetched.InboxItems);
        Assert.IsType<NotFoundObjectResult>(await monographs.GetMonographBySlugAsync(monograph.Slug!));

        await PurgeAsync();

        var repositories = db.CreateScope().Repositories;
        Assert.Equal(0, await repositories.Monographs.Collection.CountDocumentsAsync((m) => m.UserId == userId));
        Assert.Equal(0, await repositories.InboxApiKey.Collection.CountDocumentsAsync((k) => k.UserId == userId));
        Assert.Equal(0, await repositories.InboxItems.Collection.CountDocumentsAsync((i) => i.UserId == userId));
        Assert.Equal(0, await repositories.SyncDevices.Collection.CountDocumentsAsync((d) => d.UserId == userId && d.DeviceId == "old-device"));
    }

    [Fact]
    public async Task A_device_from_before_the_reset_does_a_full_fetch()
    {
        var userId = await CreateUserAsync();
        await Users().ResetUserAsync(userId, removeAttachments: false);
        await LeaveOldDataAsync(userId);
        await PushAsync(userId, "new");

        Assert.Equal(["new"], (await FetchAsync(userId, "old-device")).Items);
    }

    [Fact]
    public async Task An_inbox_key_from_before_the_reset_is_rejected()
    {
        var userId = await CreateUserAsync();
        await Users().ResetUserAsync(userId, removeAttachments: false);
        var (_, oldKey) = await LeaveOldDataAsync(userId);
        var newKey = new InboxApiKey { UserId = userId, Name = "new", Generation = 1 };
        await db.CreateScope().Repositories.InboxApiKey.Collection.InsertOneAsync(newKey);

        async Task<AuthenticateResult> AuthenticateAsync(string key)
        {
            var scope = db.CreateScope();
            var options = Stub<IOptionsMonitor<InboxApiKeyAuthenticationSchemeOptions>>.Create(("Get", (_) => new InboxApiKeyAuthenticationSchemeOptions()));
            var handler = new InboxApiKeyAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default, scope.Repositories.InboxApiKey, scope.Repositories.Generations);
            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = key;
            await handler.InitializeAsync(new AuthenticationScheme(InboxApiKeyAuthenticationDefaults.AuthenticationScheme, null, typeof(InboxApiKeyAuthenticationHandler)), context);
            return await handler.AuthenticateAsync();
        }

        Assert.False((await AuthenticateAsync(oldKey.Key)).Succeeded);
        Assert.True((await AuthenticateAsync(newKey.Key)).Succeeded);
    }

    [Fact]
    public async Task A_connection_from_before_the_reset_is_cut_off()
    {
        var userId = await CreateUserAsync();
        var scope = db.CreateScope();
        var services = new ServiceCollection().AddSingleton<ISyncItemsRepositoryAccessor>(scope.Repositories).BuildServiceProvider();
        var filter = new SyncAuthorizationHubFilter();
        var caller = new HubCaller(userId);
        await filter.OnConnectedAsync(new HubLifetimeContext(caller, services, Hub(scope, caller)), (_) => Task.CompletedTask);

        await Users().ResetUserAsync(userId, removeAttachments: false);

        var error = await Assert.ThrowsAsync<HubException>(async () => await filter.InvokeMethodAsync(
            new HubInvocationContext(caller, services, Hub(scope, caller), typeof(SyncV2Hub).GetMethod(nameof(SyncV2Hub.PushItems))!, ["device", Notes("old")]),
            (_) => throw new Exception("invoked")));

        Assert.Equal("Your account was reset. Please sync again.", error.Message);
        Assert.True(caller.Aborted);
    }

    [Fact]
    public async Task A_push_that_lands_after_the_reset_is_hidden_and_purged()
    {
        var userId = await CreateUserAsync();
        var scope = db.CreateScope();
        // pinned by the filter before the reset committed
        scope.Repositories.Generations.Pin(userId, 0);

        await Users().ResetUserAsync(userId, removeAttachments: false);
        await Hub(scope, new HubCaller(userId)).PushItems("device", Notes("in-flight"));

        Assert.Empty((await FetchAsync(userId)).Items);
        await PurgeAsync();
        Assert.Empty(await StoredNotesAsync(userId));
    }

    [Fact]
    public async Task Items_pushed_after_a_reset_survive_the_purge()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "old", "kept");
        await Users().ResetUserAsync(userId, removeAttachments: false);

        await PushAsync(userId, "new", "kept");
        await PurgeAsync();

        Assert.Equal(["kept", "new"], (await StoredNotesAsync(userId)).Order());
        Assert.Equal(["kept", "new"], (await FetchAsync(userId)).Items.Order());
        Assert.Null((await SettingsAsync(userId)).PurgePending);
    }

    [Fact]
    public async Task Items_enumerated_lazily_are_stamped_with_the_current_generation()
    {
        var userId = await CreateUserAsync();
        await Users().ResetUserAsync(userId, removeAttachments: false);

        var scope = db.CreateScope();
        scope.Repositories.Notes.UpsertMany(new[] { "lazy" }.Select(TestItems.Note), userId);
        await scope.Unit.Commit();

        Assert.Equal(["lazy"], (await FetchAsync(userId)).Items);
    }

    [Fact]
    public async Task Resetting_a_user_without_settings_fails()
    {
        await Assert.ThrowsAsync<Streetwriters.Common.Exceptions.NotFoundException>(() => Users().ResetUserAsync(Guid.NewGuid().ToString("N"), removeAttachments: false));
    }

    private static (UsersController Controller, List<string> Calls) ResetController(string userId, Func<string, object?[], object?>? fail = null)
    {
        var calls = new List<string>();
        object? Call(string name, object?[] args, object? result)
        {
            calls.Add(name);
            return fail?.Invoke(name, args) ?? result;
        }
        var accessor = new WampServiceAccessor(Servers.NotesnookAPI)
        {
            UserAccountService = Stub<IUserAccountService>.Create(
                ("LockUserAsync", (args) => Call("lock", args, Task.FromResult<long?>(42))),
                ("ClearSessionsAsync", (args) => Call($"clear:{args[2]}:{args[3]}", args, Task.FromResult(true))),
                ("UnlockUserAsync", (args) => Call($"unlock:{args[1]}", args, Task.CompletedTask)))
        };
        var users = Stub<IUserService>.Create(("ResetUserAsync", (args) => Call("reset", args, Task.CompletedTask)));
        var controller = new UsersController(users, Stub<IUserKeysService>.Create(), accessor, NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId), new Claim("jti", "this-session"), new Claim("client_id", "notesnook")], "Test")) }
            }
        };
        return (controller, calls);
    }

    [Fact]
    public async Task A_reset_ends_the_other_sessions_while_the_user_is_locked_out()
    {
        var (controller, calls) = ResetController(Guid.NewGuid().ToString("N"));

        // the logout message can't reach the messenger here, which mustn't fail the reset
        Assert.IsType<OkResult>(await controller.Reset(removeAttachments: false));

        // the lockout the user already had is restored
        Assert.Equal(["lock", "clear:False:this-session", "reset", "unlock:42"], calls);
    }

    [Fact]
    public async Task A_reset_whose_sessions_cannot_be_ended_is_aborted()
    {
        var (controller, calls) = ResetController(Guid.NewGuid().ToString("N"),
            (name, _) => name.StartsWith("clear") ? Task.FromException<bool>(new TimeoutException()) : null);

        await Assert.ThrowsAsync<Streetwriters.Common.Exceptions.ServiceUnavailableException>(() => controller.Reset(removeAttachments: false));

        Assert.Equal(["lock", "clear:False:this-session", "clear:False:this-session", "clear:False:this-session", "unlock:42"], calls);
    }

    [Fact]
    public async Task A_reset_that_cannot_lock_the_user_changes_nothing()
    {
        var (controller, calls) = ResetController(Guid.NewGuid().ToString("N"),
            (name, _) => name == "lock" ? Task.FromException<long?>(new TimeoutException()) : null);

        await Assert.ThrowsAsync<Streetwriters.Common.Exceptions.ServiceUnavailableException>(() => controller.Reset(removeAttachments: false));

        Assert.Equal(["lock", "lock", "lock"], calls);
    }

    [Fact]
    public async Task A_reset_during_a_purge_stays_pending()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "first");
        await Users().ResetUserAsync(userId, removeAttachments: false);
        await PushAsync(userId, "second");

        var real = db.CreateScope().Repositories;
        var resetDuringPurge = new Lazy<Task>(() => Users().ResetUserAsync(userId, removeAttachments: false));
        await PurgeAsync(PurgeWithSyncItems(() => { resetDuringPurge.Value.GetAwaiter().GetResult(); return real.SyncItemCollections; }));

        var settings = await SettingsAsync(userId);
        Assert.Equal(2, settings.Generation);
        Assert.True(settings.PurgePending);
        Assert.Equal(["second"], await StoredNotesAsync(userId));

        await PurgeAsync();

        Assert.Null((await SettingsAsync(userId)).PurgePending);
        Assert.Empty(await StoredNotesAsync(userId));
    }

    [Fact]
    public async Task A_failed_purge_is_retried()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "old");
        await Users().ResetUserAsync(userId, removeAttachments: false);

        await PurgeAsync(PurgeWithSyncItems(() => throw new TimeoutException()));

        Assert.True((await SettingsAsync(userId)).PurgePending);
        Assert.Equal(["old"], await StoredNotesAsync(userId));

        await PurgeAsync();

        Assert.Null((await SettingsAsync(userId)).PurgePending);
        Assert.Empty(await StoredNotesAsync(userId));
    }
}
