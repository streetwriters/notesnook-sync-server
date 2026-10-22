using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Models;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public class SyncV2HubTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private sealed class Caller(string userId) : HubCallerContext
    {
        public override string ConnectionId => "connection";
        public override string? UserIdentifier => userId;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim("sub", userId)], "Test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

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
