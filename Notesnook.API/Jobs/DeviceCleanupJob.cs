using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Quartz;

namespace Notesnook.API.Jobs
{
    public class DeviceCleanupJob(ISyncItemsRepositoryAccessor repositories) : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var cutoff = DateTimeOffset.UtcNow.AddMonths(-1).ToUnixTimeMilliseconds();
            using var cursor = await repositories.SyncDevices.Collection.Find(Builders<SyncDevice>.Filter.Lt(x => x.LastAccessTime, cutoff), new FindOptions { BatchSize = 1000 })
                .ToCursorAsync(context.CancellationToken);

            while (await cursor.MoveNextAsync(context.CancellationToken))
            {
                foreach (var device in cursor.Current)
                    await DeleteIfInactiveAsync(device.Id, device.UserId, device.DeviceId, cutoff, context.CancellationToken);
            }
        }

        /// <summary>
        /// Deletes the device and its ids in one transaction, and only if it is still inactive. A device that
        /// syncs in the meantime keeps both; deleting the ids on their own would leave it with IsSyncReset=false
        /// and missing updates.
        /// </summary>
        public async Task<bool> DeleteIfInactiveAsync(ObjectId id, string userId, string deviceId, long cutoff, CancellationToken cancellationToken = default)
        {
            using var session = await repositories.SyncDevices.Collection.Database.Client.StartSessionAsync(cancellationToken: cancellationToken);
            return await session.WithTransactionAsync(async (handle, ct) =>
            {
                var deleted = await repositories.SyncDevices.Collection.DeleteOneAsync(handle,
                    Builders<SyncDevice>.Filter.Eq(x => x.Id, id) & Builders<SyncDevice>.Filter.Lt(x => x.LastAccessTime, cutoff),
                    cancellationToken: ct);
                if (deleted.DeletedCount == 0) return false;

                await repositories.DeviceIdsChunks.Collection.DeleteManyAsync(handle,
                    Builders<DeviceIdsChunk>.Filter.Eq(x => x.UserId, userId) & Builders<DeviceIdsChunk>.Filter.Eq(x => x.DeviceId, deviceId),
                    cancellationToken: ct);
                return true;
            }, cancellationToken: cancellationToken);
        }
    }
}
