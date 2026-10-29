using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public class SyncV2HubTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private sealed class Caller(string userId, params Claim[] claims) : HubCallerContext
    {
        public override string ConnectionId => "connection";
        public override string? UserIdentifier => userId;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim("sub", userId), .. claims], "Test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private static readonly Claim[] SyncClaims = [new("scope", "notesnook.sync"), new("aud", "notesnook"), new("role", "notesnook"), new("verified", "true")];
    private static Caller SyncCaller(string userId) => new(userId, SyncClaims);

    private SyncV2Hub Hub(string userId)
    {
        var scope = db.CreateScope();
        return new SyncV2Hub(scope.Repositories, scope.Unit, scope.Devices, NullLogger<SyncV2Hub>.Instance) { Context = new Caller(userId) };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task A_missing_device_id_is_rejected(string? deviceId)
    {
        var userId = Guid.NewGuid().ToString("N");

        var fetch = await Assert.ThrowsAsync<HubException>(() => Hub(userId).RequestFetchV4(deviceId!));
        var push = await Assert.ThrowsAsync<HubException>(() => Hub(userId).PushItems(deviceId!, new SyncTransferItemV2 { Type = "note", Items = [], Count = 0 }));

        Assert.Equal("Missing device ID.", fetch.Message);
        Assert.Equal("Missing device ID.", push.Message);
        Assert.Equal(0, await db.CreateScope().Repositories.SyncDevices.Collection.CountDocumentsAsync((d) => d.UserId == userId));
    }

    [Fact]
    public async Task Connecting_requires_sync_access()
    {
        var userId = Guid.NewGuid().ToString("N");
        var filter = new SyncAuthorizationHubFilter(new MemoryCache(new MemoryCacheOptions()));
        var services = new ServiceCollection().BuildServiceProvider();

        var error = await Assert.ThrowsAsync<HubException>(() => filter.OnConnectedAsync(
            new HubLifetimeContext(new Caller(userId, SyncClaims[..^1]), services, Hub(userId)),
            (_) => throw new Exception("connected")));

        Assert.Equal("Please confirm your email to sync your notes.", error.Message);
    }

    [Fact]
    public async Task A_deleted_user_is_cut_off_but_a_live_user_is_not()
    {
        var (deleted, live) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        var scope = db.CreateScope();
        await scope.Repositories.UsersSettings.Collection.InsertManyAsync([
            new UserSettings { UserId = deleted, Salt = "salt", DeletedAt = 1 },
            new UserSettings { UserId = live, Salt = "salt" }]);
        var services = new ServiceCollection().AddSingleton<ISyncItemsRepositoryAccessor>(scope.Repositories).BuildServiceProvider();
        var filter = new SyncAuthorizationHubFilter(new MemoryCache(new MemoryCacheOptions()));
        var connected = new List<string>();
        Task Connect(string userId) => filter.OnConnectedAsync(new HubLifetimeContext(SyncCaller(userId), services, Hub(userId)), (_) => { connected.Add(userId); return Task.CompletedTask; });

        var error = await Assert.ThrowsAsync<HubException>(() => Connect(deleted));
        await Connect(live);

        Assert.Equal("Please login to sync.", error.Message);
        Assert.Equal([live], connected);
    }

    [Fact]
    public async Task A_monograph_without_an_item_id_is_sent_by_its_object_id()
    {
        var userId = Guid.NewGuid().ToString("N");
        var deviceId = "device";
        var monograph = new Monograph { UserId = userId, Title = "legacy" };
        await db.CreateScope().Repositories.Monographs.Collection.InsertOneAsync(monograph);
        var devices = db.CreateScope().Devices;
        await devices.RegisterDeviceAsync(userId, deviceId);
        await devices.ResetAsync(userId, deviceId);
        // a malformed ID alongside it must not break the fetch either
        await devices.AddIdsToAllDevicesAsync(userId, [new ItemKey(monograph.Id, "monograph"), new ItemKey("not-an-object-id", "monograph")]);

        var sent = new List<MonographMetadata>();
        var client = Stub<ISyncV2HubClient>.Create(("SendMonographs", (args) =>
        {
            sent.AddRange((IEnumerable<MonographMetadata>)args[0]!);
            return Task.FromResult(true);
        }));
        var hub = Hub(userId);
        hub.Clients = Stub<IHubCallerClients<ISyncV2HubClient>>.Create(("get_Caller", (_) => client));

        var result = await hub.RequestFetchV4(deviceId);

        Assert.True(result.Synced);
        Assert.Equal(monograph.Id, Assert.Single(sent).ItemId);
    }
}
