using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        public bool Aborted { get; private set; }
        public override void Abort() => Aborted = true;
    }

    private static readonly Claim[] SyncClaims = [new("scope", "notesnook.sync"), new("aud", "notesnook"), new("role", "notesnook"), new("verified", "true")];
    private static Caller SyncCaller(string userId) => new(userId, SyncClaims);

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    // the meter is static and tests run in parallel, so other tests' measurements can show up here too
    private sealed class Reasons : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<string> recorded = [];

        public Reasons(string instrument)
        {
            listener.InstrumentPublished = (i, l) =>
            {
                if (i.Meter.Name == "Notesnook.API.Metrics.Sync" && i.Name == instrument) l.EnableMeasurementEvents(i);
            };
            listener.SetMeasurementEventCallback<int>((_, _, tags, _) =>
            {
                foreach (var tag in tags) if (tag.Key == "reason") lock (recorded) recorded.Add((string)tag.Value!);
            });
            listener.Start();
        }

        public List<string> Recorded { get { lock (recorded) return [.. recorded]; } }
        public void Dispose() => listener.Dispose();
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
        var push = await Assert.ThrowsAsync<HubException>(() => Hub(userId).PushItems(deviceId!, new SyncTransferPushItemV2 { Type = "note", Items = [], Count = 0 }));

        Assert.Equal("Missing device ID.", fetch.Message);
        Assert.Equal("Missing device ID.", push.Message);
        Assert.Equal(0, await db.CreateScope().Repositories.SyncDevices.Collection.CountDocumentsAsync((d) => d.UserId == userId));
    }

    [Fact]
    public async Task Connecting_requires_sync_access()
    {
        var userId = Guid.NewGuid().ToString("N");
        var filter = new SyncAuthorizationHubFilter(NullLogger<SyncAuthorizationHubFilter>.Instance);
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
        var filter = new SyncAuthorizationHubFilter(NullLogger<SyncAuthorizationHubFilter>.Instance);
        var connected = new List<string>();
        Task Connect(string userId) => filter.OnConnectedAsync(new HubLifetimeContext(SyncCaller(userId), services, Hub(userId)), (_) => { connected.Add(userId); return Task.CompletedTask; });

        var error = await Assert.ThrowsAsync<HubException>(() => Connect(deleted));
        await Connect(live);

        Assert.Equal("Please login to sync.", error.Message);
        Assert.Equal([live], connected);
    }

    [Fact]
    public async Task An_open_connection_is_cut_off_once_its_user_is_deleted()
    {
        var userId = Guid.NewGuid().ToString("N");
        var scope = db.CreateScope();
        await scope.Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings { UserId = userId, Salt = "salt" });
        var services = new ServiceCollection().AddSingleton<ISyncItemsRepositoryAccessor>(scope.Repositories).BuildServiceProvider();
        var filter = new SyncAuthorizationHubFilter(NullLogger<SyncAuthorizationHubFilter>.Instance);
        var caller = SyncCaller(userId);
        var invoked = 0;
        ValueTask<object?> Invoke() => filter.InvokeMethodAsync(
            new HubInvocationContext(caller, services, Hub(userId), typeof(SyncV2Hub).GetMethod(nameof(SyncV2Hub.PushCompleted))!, []),
            (_) => { invoked++; return ValueTask.FromResult<object?>(null); });

        await Invoke();
        await scope.Repositories.UsersSettings.Collection.UpdateOneAsync((s) => s.UserId == userId, Builders<UserSettings>.Update.Set((s) => s.DeletedAt, 1));

        var error = await Assert.ThrowsAsync<HubException>(async () => await Invoke());

        Assert.Equal("Please login to sync.", error.Message);
        Assert.Equal(1, invoked);
        Assert.True(caller.Aborted);
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

    private async Task<List<SyncItem>> InsertNotesAsync(string userId, int count, int cipherLength, long length)
    {
        var notes = Enumerable.Range(0, count).Select((_) => new SyncItem
        {
            UserId = userId,
            ItemId = Guid.NewGuid().ToString("N"),
            Cipher = new string('a', cipherLength),
            IV = "iv",
            Length = length,
            Version = 5.9,
            Algorithm = Algorithms.Default
        }).ToList();
        await db.CreateScope().Repositories.Notes.Collection.InsertManyAsync(notes);
        return notes;
    }

    private SyncV2Hub HubWithClient(string userId, List<SyncTransferRawItemV2> sent, Func<int, bool>? accept = null)
    {
        var client = Stub<ISyncV2HubClient>.Create(
            ("SendItems", (args) =>
            {
                sent.Add((SyncTransferRawItemV2)args[0]!);
                return Task.FromResult(accept?.Invoke(sent.Count) ?? true);
            }),
            ("SendMonographs", (_) => Task.FromResult(true)),
            ("SendInboxItems", (_) => Task.FromResult(true)));
        var hub = Hub(userId);
        hub.Clients = Stub<IHubCallerClients<ISyncV2HubClient>>.Create(("get_Caller", (_) => client));
        return hub;
    }

    [Fact]
    public async Task A_full_sync_chunks_items_by_their_stored_size_not_the_declared_length()
    {
        var userId = Guid.NewGuid().ToString("N");
        // each item claims to be 1 byte, so chunking by Length would send all of them in one message
        var notes = await InsertNotesAsync(userId, 8, 1024 * 1024, length: 1);
        await db.CreateScope().Devices.RegisterDeviceAsync(userId, "new");
        var sent = new List<SyncTransferRawItemV2>();

        await HubWithClient(userId, sent).RequestFetchV4("new");

        var chunks = sent.Where((c) => c.Type == "note").ToList();
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, (c) => Assert.True(c.Items.Count() <= 3));
        Assert.Equal(notes.Select((n) => n.ItemId).Order(), chunks.SelectMany((c) => c.Items).Select((i) => i.ItemId).Order());
        Assert.Equal(notes.Count, chunks[^1].Count);
    }

    [Fact]
    public async Task Small_items_are_capped_per_chunk()
    {
        var userId = Guid.NewGuid().ToString("N");
        await InsertNotesAsync(userId, 1200, 100, length: 100);
        await db.CreateScope().Devices.RegisterDeviceAsync(userId, "new");
        var sent = new List<SyncTransferRawItemV2>();

        await HubWithClient(userId, sent).RequestFetchV4("new");

        Assert.Equal([500, 500, 200], sent.Where((c) => c.Type == "note").Select((c) => c.Items.Count()));
    }

    [Fact]
    public async Task An_incremental_fetch_sends_only_the_pending_items_and_clears_them()
    {
        var userId = Guid.NewGuid().ToString("N");
        var notes = await InsertNotesAsync(userId, 5, 100, length: 100);
        var devices = db.CreateScope().Devices;
        await devices.RegisterDeviceAsync(userId, "device");
        await devices.ResetAsync(userId, "device");
        var changed = notes.Take(3).ToList();
        await devices.AddIdsToAllDevicesAsync(userId, changed.Select((n) => new ItemKey(n.ItemId!, "note")));
        var sent = new List<SyncTransferRawItemV2>();

        await HubWithClient(userId, sent).RequestFetchV4("device");

        Assert.Equal(changed.Select((n) => n.ItemId).Order(), sent.SelectMany((c) => c.Items).Select((i) => i.ItemId).Order());
        Assert.Empty(await devices.GetIdsAsync(userId, "device", "pending"));
        Assert.Empty(await devices.GetIdsAsync(userId, "device", "unsynced"));
    }

    [Fact]
    public async Task A_rejected_chunk_is_sent_again_on_the_next_fetch()
    {
        var userId = Guid.NewGuid().ToString("N");
        var notes = await InsertNotesAsync(userId, 8, 1024 * 1024, length: 1024 * 1024);
        var devices = db.CreateScope().Devices;
        await devices.RegisterDeviceAsync(userId, "device");
        await devices.ResetAsync(userId, "device");
        await devices.AddIdsToAllDevicesAsync(userId, notes.Select((n) => new ItemKey(n.ItemId!, "note")));
        var firstAttempt = new List<SyncTransferRawItemV2>();

        // the client applies the first chunk and rejects the second
        await Assert.ThrowsAsync<HubException>(() => HubWithClient(userId, firstAttempt, (n) => n == 1).RequestFetchV4("device"));
        var retry = new List<SyncTransferRawItemV2>();
        await HubWithClient(userId, retry).RequestFetchV4("device");

        var acked = firstAttempt[0].Items.Select((i) => i.ItemId);
        Assert.Equal(notes.Select((n) => n.ItemId).Except(acked).Order(), retry.SelectMany((c) => c.Items).Select((i) => i.ItemId).Order());
    }

    [Fact]
    public async Task Rejected_connections_are_logged_and_counted_by_stage()
    {
        var (live, deleted) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        var scope = db.CreateScope();
        await scope.Repositories.UsersSettings.Collection.InsertManyAsync([
            new UserSettings { UserId = live, Salt = "salt" },
            new UserSettings { UserId = deleted, Salt = "salt", DeletedAt = 1 }]);
        var services = new ServiceCollection().AddSingleton<ISyncItemsRepositoryAccessor>(scope.Repositories).BuildServiceProvider();
        var logger = new ListLogger<SyncAuthorizationHubFilter>();
        var filter = new SyncAuthorizationHubFilter(logger);
        using var reasons = new Reasons("sync.v2.connection_rejections");
        Task Connect(Caller caller, Func<HubLifetimeContext, Task> next) =>
            filter.OnConnectedAsync(new HubLifetimeContext(caller, services, Hub(caller.UserIdentifier!)), next);

        await Assert.ThrowsAsync<HubException>(() => Connect(new Caller(live, SyncClaims[..^1]), (_) => Task.CompletedTask));
        await Assert.ThrowsAsync<HubException>(() => Connect(SyncCaller(deleted), (_) => Task.CompletedTask));
        await Assert.ThrowsAsync<HubException>(() => Connect(SyncCaller(live), (_) => throw new HubException("User not found.")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Connect(SyncCaller(live), (_) => throw new InvalidOperationException("redis down")));
        await Connect(SyncCaller(live), (_) => Task.CompletedTask);

        Assert.Equal(
        [
            (LogLevel.Information, "Connection connection rejected: Please confirm your email to sync your notes."),
            (LogLevel.Information, "Connection connection rejected: Please login to sync."),
            (LogLevel.Information, "Connection connection rejected: User not found."),
            (LogLevel.Error, "OnConnectedAsync failed for connection connection"),
        ], logger.Entries);
        var recorded = reasons.Recorded;
        Assert.All(new[] { "unauthorized", "account", "hub", "error" }, (reason) => Assert.Contains(reason, recorded));
    }

    public static TheoryData<Exception, string> FailedAcks => new()
    {
        { new TimeoutException(), "timeout" },
        { new IOException("Connection 'connection' disconnected."), "disconnected" },
        { new OperationCanceledException(), "disconnected" },
        { new HubException("client threw"), "client_error" },
        { new InvalidOperationException(), "server_error" },
    };

    [Theory]
    [MemberData(nameof(FailedAcks))]
    public async Task Fetch_failures_are_counted_by_reason(Exception failure, string reason)
    {
        var userId = Guid.NewGuid().ToString("N");
        await InsertNotesAsync(userId, 1, 100, length: 100);
        await db.CreateScope().Devices.RegisterDeviceAsync(userId, "new");
        var client = Stub<ISyncV2HubClient>.Create(("SendItems", (_) => Task.FromException<bool>(failure)));
        var hub = Hub(userId);
        hub.Clients = Stub<IHubCallerClients<ISyncV2HubClient>>.Create(("get_Caller", (_) => client));
        using var reasons = new Reasons("sync.v2.fetch_failures");

        await Assert.ThrowsAnyAsync<Exception>(() => hub.RequestFetchV4("new"));

        Assert.Contains(reason, reasons.Recorded);
    }

    [Fact]
    public async Task An_item_replaced_after_a_reset_mid_fetch_is_not_sent_and_stays_pending()
    {
        var userId = Guid.NewGuid().ToString("N");
        var scope = db.CreateScope();
        await scope.Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings { UserId = userId, Salt = "salt" });
        // 1MB items: chunks of 3, so the remaining items' ids are planned before the reset lands
        var notes = await InsertNotesAsync(userId, 8, 1024 * 1024, length: 1024 * 1024);
        var devices = scope.Devices;
        await devices.RegisterDeviceAsync(userId, "device");
        await devices.ResetAsync(userId, "device");
        await devices.AddIdsToAllDevicesAsync(userId, notes.Select((n) => new ItemKey(n.ItemId!, "note")));
        var sent = new List<string>();
        string? replaced = null;
        var client = Stub<ISyncV2HubClient>.Create(
            ("SendItems", (args) =>
            {
                sent.AddRange(((SyncTransferRawItemV2)args[0]!).Items.Select((i) => i.ItemId!));
                if (replaced != null) return Task.FromResult(true);
                // the user is reset and another device pushes over an item this fetch has yet to send
                replaced = notes.Select((n) => n.ItemId!).First((id) => !sent.Contains(id));
                var repositories = db.CreateScope().Repositories;
                repositories.UsersSettings.Collection.UpdateOne((s) => s.UserId == userId, Builders<UserSettings>.Update.Set((s) => s.Generation, 1));
                repositories.Notes.Collection.UpdateOne((n) => n.UserId == userId && n.ItemId == replaced, Builders<SyncItem>.Update.Set((n) => n.Generation, 1));
                return Task.FromResult(true);
            }),
            ("SendMonographs", (_) => Task.FromResult(true)),
            ("SendInboxItems", (_) => Task.FromResult(true)));
        var hub = Hub(userId);
        hub.Clients = Stub<IHubCallerClients<ISyncV2HubClient>>.Create(("get_Caller", (_) => client));

        await hub.RequestFetchV4("device");

        Assert.NotNull(replaced);
        Assert.DoesNotContain(replaced, sent);
        Assert.Equal(notes.Count - 1, sent.Distinct().Count());
        Assert.Equal([new ItemKey(replaced, "note")], await devices.GetIdsAsync(userId, "device", "pending"));
    }
}
