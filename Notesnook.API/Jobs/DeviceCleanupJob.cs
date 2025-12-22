using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Quartz;

namespace Notesnook.API.Jobs
{
    public class DeviceCleanupJob(ISyncItemsRepositoryAccessor repositories) : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var cutoffDate = DateTimeOffset.UtcNow.AddMonths(-1).ToUnixTimeMilliseconds();
            var deviceFilter = Builders<SyncDevice>.Filter.Lt(x => x.LastAccessTime, cutoffDate);

            using var cursor = await repositories.SyncDevices.Collection.Find(deviceFilter, new FindOptions { BatchSize = 1000 })
                .Project(x => x.DeviceId)
                .ToCursorAsync();

            var deleteModels = new List<WriteModel<DeviceIdsChunk>>();
            while (await cursor.MoveNextAsync())
            {
                if (!cursor.Current.Any()) continue;
                deleteModels.Add(new DeleteManyModel<DeviceIdsChunk>(Builders<DeviceIdsChunk>.Filter.In(x => x.DeviceId, cursor.Current)));
            }

            if (deleteModels.Count > 0)
            {
                var bulkOptions = new BulkWriteOptions { IsOrdered = false };
                await repositories.DeviceIdsChunks.Collection.BulkWriteAsync(deleteModels, bulkOptions);
            }

            await repositories.SyncDevices.Collection.DeleteManyAsync(deviceFilter);
        }
    }
}