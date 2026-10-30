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
        return new UserService(Stub<IHttpContextAccessor>.Create(), scope.Repositories, scope.Unit,
            Stub<IS3Service>.Create(), scope.DbContext, new WampServiceAccessor(Servers.NotesnookAPI), NullLogger<UserService>.Instance);
    }

    private Task ResetAsync(string userId) => Users().ResetUserAsync(userId, removeAttachments: false);

    private Task EndGracePeriodAsync(string userId) =>
        db.CreateScope().Repositories.UsersSettings.Collection.UpdateOneAsync((s) => s.UserId == userId, Builders<UserSettings>.Update.Set((s) => s.PurgePendingSince, 0));

    // with collections, the sync item step runs the given action instead of deleting
    private Task PurgeAsync(Func<IReadOnlyList<SyncItemCollection>>? collections = null)
    {
        var scope = db.CreateScope();
        var real = scope.Repositories;
        var repositories = collections == null ? real : Stub<ISyncItemsRepositoryAccessor>.Create(
            ("get_UsersSettings", (_) => real.UsersSettings),
            ("get_SyncItemCollections", (_) => collections()));
        var accessor = new WampServiceAccessor(Servers.NotesnookAPI) { UserAccountService = Stub<IUserAccountService>.Create() };
        return new UserPurgeJob(repositories, scope.Devices, Stub<IS3Service>.Create(), accessor, NullLogger<UserPurgeJob>.Instance).Execute(Context);
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

    private async Task<Fetched> FetchAsync(string userId, string? deviceId = null, TestDatabase.Scope? scope = null)
    {
        var fetched = new Fetched([], [], []);
        var client = Stub<ISyncV2HubClient>.Create(
            ("SendItems", (args) => { fetched.Items.AddRange(((SyncTransferItemV2)args[0]!).Items.Select((i) => i.ItemId!)); return Task.FromResult(true); }),
            ("SendMonographs", (args) => { fetched.Monographs.AddRange(((IEnumerable<MonographMetadata>)args[0]!).Select((m) => m.ItemId!)); return Task.FromResult(true); }),
            ("SendInboxItems", (args) => { fetched.InboxItems.AddRange(((IEnumerable<InboxSyncItem>)args[0]!).Select((i) => i.ItemId!)); return Task.FromResult(true); }));
        var hub = Hub(scope ?? db.CreateScope(), new HubCaller(userId));
        hub.Clients = Stub<IHubCallerClients<ISyncV2HubClient>>.Create(("get_Caller", (_) => client));
        await hub.RequestFetchV4(deviceId ?? Guid.NewGuid().ToString("N"));
        return fetched;
    }

    private async Task AddOtherDataAsync(string userId)
    {
        var repositories = db.CreateScope().Repositories;
        await repositories.Monographs.Collection.InsertOneAsync(new Monograph { UserId = userId, ItemId = "monograph", Slug = Guid.NewGuid().ToString("N"), Title = "old" });
        await repositories.InboxApiKey.Collection.InsertOneAsync(new InboxApiKey { UserId = userId, Name = "old" });
        await repositories.InboxItems.Collection.InsertOneAsync(new InboxSyncItem { UserId = userId, ItemId = "inbox", Cipher = "cipher", Algorithm = Algorithms.Default, Version = 1 });
        await repositories.SyncDevices.Collection.InsertOneAsync(new SyncDevice { UserId = userId, DeviceId = "old-device", LastAccessTime = 0, IsSyncReset = false });
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

        await ResetAsync(userId);

        Assert.Empty((await FetchAsync(userId)).Items);
        Assert.Null(await Users().GetEncryptionVerifier(userId));
        Assert.Equal(["old"], await StoredNotesAsync(userId));
    }

    [Fact]
    public async Task A_reset_deletes_everything_but_the_sync_items_right_away()
    {
        var userId = await CreateUserAsync();
        await AddOtherDataAsync(userId);

        await ResetAsync(userId);

        var repositories = db.CreateScope().Repositories;
        Assert.Equal(0, await repositories.Monographs.Collection.CountDocumentsAsync((m) => m.UserId == userId));
        Assert.Equal(0, await repositories.InboxApiKey.Collection.CountDocumentsAsync((k) => k.UserId == userId));
        Assert.Equal(0, await repositories.InboxItems.Collection.CountDocumentsAsync((i) => i.UserId == userId));
        Assert.Equal(0, await repositories.SyncDevices.Collection.CountDocumentsAsync((d) => d.UserId == userId));
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

        await ResetAsync(userId);

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

        await ResetAsync(userId);
        await PurgeAsync();
        await Hub(scope, new HubCaller(userId)).PushItems("device", Notes("in-flight"));

        Assert.Empty((await FetchAsync(userId)).Items);
        Assert.NotNull((await SettingsAsync(userId)).PurgePendingSince);
        Assert.Equal(["in-flight"], await StoredNotesAsync(userId));
        await EndGracePeriodAsync(userId);
        await PurgeAsync();
        Assert.Empty(await StoredNotesAsync(userId));
    }

    [Fact]
    public async Task A_fetch_across_the_reset_leaves_its_device_to_sync_in_full()
    {
        var userId = await CreateUserAsync();
        var scope = db.CreateScope();
        scope.Repositories.Generations.Pin(userId, 0);

        await ResetAsync(userId);
        await FetchAsync(userId, "device", scope);

        Assert.True((await db.CreateScope().Devices.GetDeviceAsync(userId, "device"))!.IsSyncReset);
    }

    [Fact]
    public async Task Items_pushed_after_a_reset_survive_the_purge()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "old", "kept");
        await ResetAsync(userId);

        await PushAsync(userId, "new", "kept");
        await EndGracePeriodAsync(userId);
        await PurgeAsync();

        Assert.Equal(["kept", "new"], (await StoredNotesAsync(userId)).Order());
        Assert.Equal(["kept", "new"], (await FetchAsync(userId)).Items.Order());
        Assert.Null((await SettingsAsync(userId)).PurgePendingSince);
    }

    [Fact]
    public async Task Items_enumerated_lazily_are_stamped_with_the_current_generation()
    {
        var userId = await CreateUserAsync();
        await ResetAsync(userId);

        var scope = db.CreateScope();
        scope.Repositories.Notes.UpsertMany(new[] { "lazy" }.Select(TestItems.Note), userId);
        await scope.Unit.Commit();

        Assert.Equal(["lazy"], (await FetchAsync(userId)).Items);
    }

    [Fact]
    public async Task Resetting_a_user_without_settings_fails_and_deletes_nothing()
    {
        var userId = Guid.NewGuid().ToString("N");
        await AddOtherDataAsync(userId);

        await Assert.ThrowsAsync<Streetwriters.Common.Exceptions.NotFoundException>(() => Users().ResetUserAsync(userId, removeAttachments: false));

        Assert.Equal(1, await db.CreateScope().Repositories.Monographs.Collection.CountDocumentsAsync((m) => m.UserId == userId));
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
        await ResetAsync(userId);
        await PushAsync(userId, "second");

        var real = db.CreateScope().Repositories;
        var resetDuringPurge = new Lazy<Task>(() => ResetAsync(userId));
        await EndGracePeriodAsync(userId);
        await PurgeAsync(() => { resetDuringPurge.Value.GetAwaiter().GetResult(); return real.SyncItemCollections; });

        var settings = await SettingsAsync(userId);
        Assert.Equal(2, settings.Generation);
        Assert.NotNull(settings.PurgePendingSince);
        Assert.Equal(["second"], await StoredNotesAsync(userId));

        await EndGracePeriodAsync(userId);
        await PurgeAsync();

        Assert.Null((await SettingsAsync(userId)).PurgePendingSince);
        Assert.Empty(await StoredNotesAsync(userId));
    }

    [Fact]
    public async Task A_failed_purge_is_retried()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "old");
        await ResetAsync(userId);

        await EndGracePeriodAsync(userId);
        await PurgeAsync(() => throw new TimeoutException());

        Assert.NotNull((await SettingsAsync(userId)).PurgePendingSince);
        Assert.Equal(["old"], await StoredNotesAsync(userId));

        await EndGracePeriodAsync(userId);
        await PurgeAsync();

        Assert.Null((await SettingsAsync(userId)).PurgePendingSince);
        Assert.Empty(await StoredNotesAsync(userId));
    }
}
