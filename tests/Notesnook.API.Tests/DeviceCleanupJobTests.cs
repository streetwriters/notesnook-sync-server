using MongoDB.Driver;
using Notesnook.API.Jobs;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Quartz;

namespace Notesnook.API.Tests;

public class DeviceCleanupJobTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private static string NewUserId() => Guid.NewGuid().ToString("N");
    private static readonly long LongAgo = DateTimeOffset.UtcNow.AddMonths(-2).ToUnixTimeMilliseconds();

    private async Task<SyncDevice> RegisterDeviceWithIdsAsync(string userId, string deviceId, long lastAccessTime)
    {
        var scope = db.CreateScope();
        await scope.Devices.RegisterDeviceAsync(userId, deviceId);
        await scope.Devices.ResetAsync(userId, deviceId);
        await scope.Devices.WriteIdsAsync(userId, deviceId, "unsynced", [new ItemKey("item", "note")]);
        return await scope.Repositories.SyncDevices.Collection.FindOneAndUpdateAsync(
            (d) => d.UserId == userId && d.DeviceId == deviceId,
            Builders<SyncDevice>.Update.Set((d) => d.LastAccessTime, lastAccessTime),
            new FindOneAndUpdateOptions<SyncDevice> { ReturnDocument = ReturnDocument.After });
    }

    private Task<long> CountIdsAsync(string userId, string deviceId) =>
        db.CreateScope().Repositories.DeviceIdsChunks.Collection.CountDocumentsAsync((c) => c.UserId == userId && c.DeviceId == deviceId);

    private Task<long> CountDevicesAsync(string userId, string deviceId) =>
        db.CreateScope().Repositories.SyncDevices.Collection.CountDocumentsAsync((d) => d.UserId == userId && d.DeviceId == deviceId);

    [Fact]
    public async Task Deletes_an_inactive_device_with_its_ids_but_not_another_users_device_with_the_same_id()
    {
        var (inactiveUser, activeUser) = (NewUserId(), NewUserId());
        await RegisterDeviceWithIdsAsync(inactiveUser, "same", LongAgo);
        await RegisterDeviceWithIdsAsync(activeUser, "same", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await new DeviceCleanupJob(db.CreateScope().Repositories)
            .Execute(Stub<IJobExecutionContext>.Create(("get_CancellationToken", (_) => CancellationToken.None)));

        Assert.Equal(0, await CountDevicesAsync(inactiveUser, "same"));
        Assert.Equal(0, await CountIdsAsync(inactiveUser, "same"));
        Assert.Equal(1, await CountDevicesAsync(activeUser, "same"));
        Assert.Equal(1, await CountIdsAsync(activeUser, "same"));
    }

    [Fact]
    public async Task A_device_that_syncs_after_being_found_inactive_keeps_its_ids()
    {
        var userId = NewUserId();
        var stale = await RegisterDeviceWithIdsAsync(userId, "returning", LongAgo);
        // the device syncs between the job finding it and deleting it
        await db.CreateScope().Devices.UpdateLastAccessTimeAsync(userId, "returning");

        var deleted = await new DeviceCleanupJob(db.CreateScope().Repositories)
            .DeleteIfInactiveAsync(stale.Id, userId, "returning", DateTimeOffset.UtcNow.AddMonths(-1).ToUnixTimeMilliseconds());

        Assert.False(deleted);
        Assert.Equal(1, await CountDevicesAsync(userId, "returning"));
        Assert.Equal(1, await CountIdsAsync(userId, "returning"));
    }
}
