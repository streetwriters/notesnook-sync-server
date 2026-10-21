using MongoDB.Driver;
using Notesnook.API.Models;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public class SyncDeviceServiceTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private static string NewUserId() => Guid.NewGuid().ToString("N");
    private static ItemKey Note(string id) => new(id, "note");

    private async Task<SyncDeviceService> RegisterSyncedDeviceAsync(string userId, string deviceId)
    {
        var devices = db.CreateScope().Devices;
        await devices.RegisterDeviceAsync(userId, deviceId);
        // finish the initial full sync so the device gets incremental fetches
        await devices.FetchUnsyncedIdsAsync(userId, deviceId);
        await devices.ResetAsync(userId, deviceId);
        return devices;
    }

    [Fact]
    public async Task Fetch_merges_unsynced_into_pending()
    {
        var userId = NewUserId();
        var devices = await RegisterSyncedDeviceAsync(userId, "B");
        await devices.WriteIdsAsync(userId, "B", "pending", [Note("pending")]);
        await devices.AddIdsToOtherDevicesAsync(userId, "A", [Note("unsynced")]);

        var ids = await devices.FetchUnsyncedIdsAsync(userId, "B");

        Assert.Equal([Note("pending"), Note("unsynced")], ids.OrderBy(i => i.ItemId));
        Assert.Empty(await devices.GetIdsAsync(userId, "B", "unsynced"));
        Assert.Equal(ids, await devices.GetIdsAsync(userId, "B", "pending"));
    }

    // #23: sent ids are pulled from every "pending" chunk and emptied chunks are deleted
    [Fact]
    public async Task Removing_pending_ids_pulls_them_from_every_chunk()
    {
        var userId = NewUserId();
        var scope = db.CreateScope();
        var devices = await RegisterSyncedDeviceAsync(userId, "B");
        await devices.WriteIdsAsync(userId, "B", "pending", [Note("1"), Note("2")]);
        await devices.AddIdsToOtherDevicesAsync(userId, "A", [Note("1"), Note("3")]);
        await devices.FetchUnsyncedIdsAsync(userId, "B");

        await devices.RemovePendingIdsAsync(userId, "B", [Note("1"), Note("3")]);

        Assert.Equal([Note("2")], await devices.GetIdsAsync(userId, "B", "pending"));
        Assert.Equal(1, await scope.Repositories.DeviceIdsChunks.Collection.CountDocumentsAsync(c => c.UserId == userId && c.DeviceId == "B" && c.Key == "pending"));
    }

    // #23: removing sent ids must not touch ids recorded in "unsynced" while they were being sent
    [Fact]
    public async Task Removing_pending_ids_keeps_unsynced_ids()
    {
        var userId = NewUserId();
        var devices = await RegisterSyncedDeviceAsync(userId, "B");
        await devices.AddIdsToOtherDevicesAsync(userId, "A", [Note("1")]);
        await devices.FetchUnsyncedIdsAsync(userId, "B");
        await devices.AddIdsToOtherDevicesAsync(userId, "A", [Note("1")]);

        await devices.RemovePendingIdsAsync(userId, "B", [Note("1")]);

        Assert.Empty(await devices.GetIdsAsync(userId, "B", "pending"));
        Assert.Equal([Note("1")], await devices.GetIdsAsync(userId, "B", "unsynced"));
    }

    // #7: ids appended while a fetch moves "unsynced" to "pending" must not be lost
    [Fact]
    public async Task Ids_appended_during_fetches_are_never_lost()
    {
        var userId = NewUserId();
        await RegisterSyncedDeviceAsync(userId, "B");

        const int pushers = 4;
        const int pushesPerPusher = 150;
        var pushing = Enumerable.Range(0, pushers).Select(p => Task.Run(async () =>
        {
            for (var i = 0; i < pushesPerPusher; i++)
                await db.CreateScope().Devices.AddIdsToOtherDevicesAsync(userId, "A", [Note($"{p}-{i}")]);
        })).ToArray();
        var allPushed = Task.WhenAll(pushing);

        var received = new HashSet<ItemKey>();
        async Task FetchAsync()
        {
            var devices = db.CreateScope().Devices;
            received.UnionWith(await devices.FetchUnsyncedIdsAsync(userId, "B"));
            await devices.ResetAsync(userId, "B");
        }

        while (!allPushed.IsCompleted) await FetchAsync();
        await allPushed;
        await FetchAsync();

        var expected = Enumerable.Range(0, pushers).SelectMany(p => Enumerable.Range(0, pushesPerPusher).Select(i => Note($"{p}-{i}")));
        Assert.Empty(expected.Except(received));
    }

    // #8: a device in the middle of a full sync must still get ids recorded
    [Fact]
    public async Task Ids_are_recorded_for_devices_in_a_full_sync()
    {
        var userId = NewUserId();
        var devices = db.CreateScope().Devices;
        await devices.RegisterDeviceAsync(userId, "B");

        await devices.AddIdsToOtherDevicesAsync(userId, "A", [Note("1")]);
        await devices.AddIdsToAllDevicesAsync(userId, [Note("2")]);

        Assert.Equal([Note("1"), Note("2")], (await devices.GetIdsAsync(userId, "B", "unsynced")).OrderBy(i => i.ItemId));
    }

    // #8: ids recorded before a full sync are redundant, ids recorded during it must be sent afterwards
    [Fact]
    public async Task Full_sync_drops_earlier_ids_and_keeps_ids_recorded_during_it()
    {
        var userId = NewUserId();
        var devices = db.CreateScope().Devices;
        await devices.RegisterDeviceAsync(userId, "B");
        await devices.AddIdsToOtherDevicesAsync(userId, "A", [Note("before")]);

        // full sync starts
        Assert.Empty(await devices.FetchUnsyncedIdsAsync(userId, "B"));
        Assert.Empty(await devices.GetIdsAsync(userId, "B", "unsynced"));
        // another device edits an item the full sync has already sent
        await devices.AddIdsToOtherDevicesAsync(userId, "A", [Note("during")]);
        // full sync completes
        await devices.ResetAsync(userId, "B");

        Assert.False((await devices.GetDeviceAsync(userId, "B"))!.IsSyncReset);
        Assert.Equal([Note("during")], await devices.FetchUnsyncedIdsAsync(userId, "B"));
    }

    // #11: the ids are recorded in the same unit of work as the pushed items
    [Fact]
    public async Task Queued_ids_are_recorded_on_commit()
    {
        var userId = NewUserId();
        await RegisterSyncedDeviceAsync(userId, "A");
        await RegisterSyncedDeviceAsync(userId, "B");

        var scope = db.CreateScope();
        scope.Repositories.Notes.UpsertMany([TestItems.Note("1")], userId);
        scope.Devices.QueueAddIdsToOtherDevices(userId, "A", [Note("1")]);

        Assert.Empty(await scope.Devices.GetIdsAsync(userId, "B", "unsynced"));
        Assert.True(await scope.Unit.Commit());

        Assert.Equal(1, await scope.Repositories.Notes.Collection.CountDocumentsAsync(i => i.UserId == userId));
        Assert.Equal([Note("1")], await scope.Devices.GetIdsAsync(userId, "B", "unsynced"));
        Assert.Empty(await scope.Devices.GetIdsAsync(userId, "A", "unsynced"));
    }

    // #11: if the push fails, no ids must be recorded either (and vice versa)
    [Fact]
    public async Task Queued_ids_are_rolled_back_with_the_items()
    {
        var userId = NewUserId();
        await RegisterSyncedDeviceAsync(userId, "B");

        var scope = db.CreateScope();
        scope.Repositories.Notes.UpsertMany([TestItems.Note("1")], userId);
        scope.Devices.QueueAddIdsToOtherDevices(userId, "A", [Note("1")]);
        scope.DbContext.AddCommand((_, _) => throw new InvalidOperationException("push failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Unit.Commit());

        Assert.Equal(0, await scope.Repositories.Notes.Collection.CountDocumentsAsync(i => i.UserId == userId));
        Assert.Empty(await scope.Devices.GetIdsAsync(userId, "B", "unsynced"));
    }

    // #11: a fetch moving chunks while a push transaction appends to them must not lose the push's ids
    [Fact]
    public async Task Queued_ids_survive_concurrent_fetches()
    {
        var userId = NewUserId();
        await RegisterSyncedDeviceAsync(userId, "B");

        const int pushes = 200;
        var pushing = Task.Run(async () =>
        {
            for (var i = 0; i < pushes; i++)
            {
                var scope = db.CreateScope();
                scope.Repositories.Notes.UpsertMany([TestItems.Note($"{i}")], userId);
                scope.Devices.QueueAddIdsToOtherDevices(userId, "A", [Note($"{i}")]);
                await scope.Unit.Commit();
            }
        });

        var received = new HashSet<ItemKey>();
        async Task FetchAsync()
        {
            var devices = db.CreateScope().Devices;
            received.UnionWith(await devices.FetchUnsyncedIdsAsync(userId, "B"));
            await devices.ResetAsync(userId, "B");
        }

        while (!pushing.IsCompleted) await FetchAsync();
        await pushing;
        await FetchAsync();

        Assert.Empty(Enumerable.Range(0, pushes).Select(i => Note($"{i}")).Except(received));
    }
}
