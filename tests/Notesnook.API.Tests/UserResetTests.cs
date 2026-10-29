using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Jobs;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Quartz;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Exceptions;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Tests;

public class UserResetTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private sealed class HubCaller(string userId) : HubCallerContext
    {
        public override string ConnectionId => "connection";
        public override string? UserIdentifier => userId;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim("sub", userId)], "Test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private static IJobExecutionContext Context => Stub<IJobExecutionContext>.Create(("get_CancellationToken", (_) => CancellationToken.None));

    private UserService Users()
    {
        var scope = db.CreateScope();
        return new UserService(Stub<Microsoft.AspNetCore.Http.IHttpContextAccessor>.Create(), scope.Repositories,
            Stub<IS3Service>.Create(), scope.Devices, new WampServiceAccessor(Servers.NotesnookAPI), NullLogger<UserService>.Instance);
    }

    private Task PurgeAsync(ISyncItemsRepositoryAccessor? repositories = null)
    {
        var scope = db.CreateScope();
        var accessor = new WampServiceAccessor(Servers.NotesnookAPI) { UserAccountService = Stub<IUserAccountService>.Create() };
        return new UserPurgeJob(repositories ?? scope.Repositories, scope.Devices, Stub<IS3Service>.Create(), accessor, NullLogger<UserPurgeJob>.Instance).Execute(Context);
    }

    private async Task<string> CreateUserAsync()
    {
        var userId = Guid.NewGuid().ToString("N");
        // without Generation, like settings stored before generations existed
        await db.Collection<BsonDocument>("user_settings").InsertOneAsync(new BsonDocument { { "UserId", userId }, { "Salt", "salt" } });
        return userId;
    }

    private async Task PushAsync(string userId, params string[] itemIds)
    {
        var scope = db.CreateScope();
        var hub = new SyncV2Hub(scope.Repositories, scope.Unit, scope.Devices, NullLogger<SyncV2Hub>.Instance) { Context = new HubCaller(userId) };
        Assert.Equal(1, await hub.PushItems("device", new SyncTransferItemV2 { Type = "note", Items = [.. itemIds.Select(TestItems.Note)], Count = itemIds.Length }));
    }

    private async Task<List<string>> FetchAsync(string userId)
    {
        var scope = db.CreateScope();
        var sent = new List<string>();
        var client = Stub<ISyncV2HubClient>.Create(("SendItems", (args) =>
        {
            sent.AddRange(((SyncTransferItemV2)args[0]!).Items.Select((i) => i.ItemId!));
            return Task.FromResult(true);
        }));
        var hub = new SyncV2Hub(scope.Repositories, scope.Unit, scope.Devices, NullLogger<SyncV2Hub>.Instance)
        {
            Context = new HubCaller(userId),
            Clients = Stub<IHubCallerClients<ISyncV2HubClient>>.Create(("get_Caller", (_) => client))
        };
        // a new device fetches everything
        await hub.RequestFetchV4(Guid.NewGuid().ToString("N"));
        return sent;
    }

    private Task<List<string>> StoredNotesAsync(string userId) =>
        db.CreateScope().Repositories.Notes.Collection.Find((i) => i.UserId == userId).Project((i) => i.ItemId!).ToListAsync();

    private Task<UserSettings> SettingsAsync(string userId) =>
        db.CreateScope().Repositories.UsersSettings.Collection.Find((s) => s.UserId == userId).FirstAsync();

    [Fact]
    public async Task A_reset_hides_old_items_straight_away()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "old");
        Assert.NotNull(await Users().GetEncryptionVerifier(userId));

        await Users().ResetUserAsync(userId, removeAttachments: false);

        Assert.Empty(await FetchAsync(userId));
        Assert.Null(await Users().GetEncryptionVerifier(userId));
        Assert.Equal(["old"], await StoredNotesAsync(userId));
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
        Assert.Equal(["kept", "new"], (await FetchAsync(userId)).Order());
        Assert.Null((await SettingsAsync(userId)).PurgePending);
    }

    [Fact]
    public async Task A_push_overlapping_a_reset_is_rejected()
    {
        var userId = await CreateUserAsync();
        var scope = db.CreateScope();
        var attempts = 0;
        // runs first in the push's transaction, so the reset commits after the transaction has started
        scope.DbContext.AddCommand(async (session, ct) =>
        {
            if (attempts++ > 0) return;
            await scope.Repositories.UsersSettings.Collection.Find(session, (s) => s.UserId == userId).FirstAsync(ct);
            await Users().ResetUserAsync(userId, removeAttachments: false);
        });
        scope.Repositories.Notes.UpsertMany([TestItems.Note("in-flight")], userId);

        await Assert.ThrowsAsync<ConflictException>(() => scope.Unit.Commit());

        Assert.Equal(2, attempts);
        Assert.Empty(await StoredNotesAsync(userId));
    }

    [Fact]
    public async Task Items_enumerated_lazily_are_stamped_with_the_current_generation()
    {
        var userId = await CreateUserAsync();
        await Users().ResetUserAsync(userId, removeAttachments: false);

        var scope = db.CreateScope();
        scope.Repositories.Notes.UpsertMany(new[] { "lazy" }.Select(TestItems.Note), userId);
        await scope.Unit.Commit();

        Assert.Equal(["lazy"], await FetchAsync(userId));
    }

    [Fact]
    public async Task A_reset_during_a_purge_is_not_marked_as_done()
    {
        var userId = await CreateUserAsync();
        await PushAsync(userId, "first");
        await Users().ResetUserAsync(userId, removeAttachments: false);
        await PushAsync(userId, "second");

        var real = db.CreateScope().Repositories;
        var resetDuringPurge = new Lazy<Task>(() => Users().ResetUserAsync(userId, removeAttachments: false));
        var repositories = Stub<ISyncItemsRepositoryAccessor>.Create(
            ("get_UsersSettings", (_) => real.UsersSettings),
            ("get_SyncItemCollections", (_) => { resetDuringPurge.Value.GetAwaiter().GetResult(); return real.SyncItemCollections; }));

        await PurgeAsync(repositories);

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

        var real = db.CreateScope().Repositories;
        var repositories = Stub<ISyncItemsRepositoryAccessor>.Create(
            ("get_UsersSettings", (_) => real.UsersSettings),
            ("get_SyncItemCollections", (_) => throw new TimeoutException()));

        await PurgeAsync(repositories);

        Assert.True((await SettingsAsync(userId)).PurgePending);
        Assert.Equal(["old"], await StoredNotesAsync(userId));

        await PurgeAsync();

        Assert.Null((await SettingsAsync(userId)).PurgePending);
        Assert.Empty(await StoredNotesAsync(userId));
    }
}
