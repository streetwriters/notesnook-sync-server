/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Services
{
    public readonly record struct ItemKey(string ItemId, string Type)
    {
        public override string ToString() => $"{ItemId}:{Type}";
    }
    public class SyncDeviceService(ISyncItemsRepositoryAccessor repositories, IDbContext dbContext, ILogger<SyncDeviceService> logger)
    {
        private static FilterDefinition<SyncDevice> DeviceFilter(string userId, string deviceId) =>
            Builders<SyncDevice>.Filter.Eq(x => x.UserId, userId) &
            Builders<SyncDevice>.Filter.Eq(x => x.DeviceId, deviceId);
        private static FilterDefinition<DeviceIdsChunk> DeviceIdsChunkFilter(string userId, string deviceId, string key) =>
            Builders<DeviceIdsChunk>.Filter.Eq(x => x.UserId, userId) &
            Builders<DeviceIdsChunk>.Filter.Eq(x => x.DeviceId, deviceId) &
            Builders<DeviceIdsChunk>.Filter.Eq(x => x.Key, key);

        private static FilterDefinition<DeviceIdsChunk> DeviceIdsChunkFilter(string userId, string deviceId) =>
            Builders<DeviceIdsChunk>.Filter.Eq(x => x.UserId, userId) &
            Builders<DeviceIdsChunk>.Filter.Eq(x => x.DeviceId, deviceId);

        private static FilterDefinition<DeviceIdsChunk> DeviceIdsChunkFilter(string userId) =>
            Builders<DeviceIdsChunk>.Filter.Eq(x => x.UserId, userId);

        private static FilterDefinition<SyncDevice> UserFilter(string userId) => Builders<SyncDevice>.Filter.Eq(x => x.UserId, userId);


        public async Task<HashSet<ItemKey>> GetIdsAsync(string userId, string deviceId, string key)
        {
            var cursor = await repositories.DeviceIdsChunks.Collection.FindAsync(DeviceIdsChunkFilter(userId, deviceId, key));
            var result = new HashSet<ItemKey>();
            while (await cursor.MoveNextAsync())
            {
                foreach (var chunk in cursor.Current)
                {
                    foreach (var id in chunk.Ids)
                    {
                        var parts = id.Split(':', 2);
                        result.Add(new ItemKey(parts[0], parts[1]));
                    }
                }
            }
            return result;
        }

        const int MaxIdsPerChunk = 25_000;
        public async Task AppendIdsAsync(IClientSessionHandle session, string userId, string deviceId, string key, IEnumerable<ItemKey> ids, CancellationToken ct = default)
        {
            // write concern can't be set on operations inside a transaction
            var collection = session.IsInTransaction
                ? repositories.DeviceIdsChunks.Collection
                : repositories.DeviceIdsChunks.Collection.WithWriteConcern(WriteConcern.W1);

            var filter = DeviceIdsChunkFilter(userId, deviceId, key) & Builders<DeviceIdsChunk>.Filter.Where(x => x.Ids.Length < MaxIdsPerChunk);
            var chunk = await collection.Find(session, filter).FirstOrDefaultAsync(ct);

            // the chunk may have been moved to "pending" (or deleted) after we found it,
            // so only update it if it is still "unsynced" and fall back to a new chunk otherwise
            var updated = false;
            if (chunk != null)
            {
                var update = Builders<DeviceIdsChunk>.Update.AddToSetEach(x => x.Ids, ids.Select(i => i.ToString()));
                var result = await collection.UpdateOneAsync(
                    session,
                    Builders<DeviceIdsChunk>.Filter.Eq(x => x.Id, chunk.Id) & Builders<DeviceIdsChunk>.Filter.Eq(x => x.Key, key),
                    update,
                    cancellationToken: ct
                );
                updated = result.MatchedCount > 0;
            }

            if (!updated)
            {
                var newChunk = new DeviceIdsChunk
                {
                    UserId = userId,
                    DeviceId = deviceId,
                    Key = key,
                    Ids = [.. ids.Select(i => i.ToString())]
                };
                await collection.InsertOneAsync(session, newChunk, cancellationToken: ct);
            }

            var emptyChunksFilter = DeviceIdsChunkFilter(userId, deviceId, key) & Builders<DeviceIdsChunk>.Filter.Size(x => x.Ids, 0);
            await collection.DeleteManyAsync(session, emptyChunksFilter, cancellationToken: ct);
        }

        public async Task WriteIdsAsync(string userId, string deviceId, string key, IEnumerable<ItemKey> ids)
        {
            var writes = new List<WriteModel<DeviceIdsChunk>>
            {
                new DeleteManyModel<DeviceIdsChunk>(DeviceIdsChunkFilter(userId, deviceId, key))
            };
            var chunks = ids.Chunk(MaxIdsPerChunk);
            foreach (var chunk in chunks)
            {
                var newChunk = new DeviceIdsChunk
                {
                    UserId = userId,
                    DeviceId = deviceId,
                    Key = key,
                    Ids = [.. chunk.Select(i => i.ToString())]
                };
                writes.Add(new InsertOneModel<DeviceIdsChunk>(newChunk));
            }
            await repositories.DeviceIdsChunks.Collection.WithWriteConcern(WriteConcern.W1).BulkWriteAsync(writes);
        }

        public async Task<HashSet<ItemKey>> FetchUnsyncedIdsAsync(string userId, string deviceId)
        {
            var device = await GetDeviceAsync(userId, deviceId);
            if (device == null) return [];
            if (device.IsSyncReset)
            {
                // starting a full sync: it will send every item as it is now, so ids recorded
                // up to this point are redundant. ids appended after this line survive the sync
                // (ResetAsync only clears "pending") and are sent on the next fetch.
                await repositories.DeviceIdsChunks.Collection.DeleteManyAsync(DeviceIdsChunkFilter(userId, deviceId));
                return [];
            }

            // atomically move each "unsynced" chunk to "pending" so ids appended concurrently are never lost
            await repositories.DeviceIdsChunks.Collection.UpdateManyAsync(
                DeviceIdsChunkFilter(userId, deviceId, "unsynced"),
                Builders<DeviceIdsChunk>.Update.Set(x => x.Key, "pending")
            );

            return await GetIdsAsync(userId, deviceId, "pending");
        }

        public async Task WritePendingIdsAsync(string userId, string deviceId, HashSet<ItemKey> ids)
        {
            await WriteIdsAsync(userId, deviceId, "pending", ids);
        }

        public async Task ResetAsync(string userId, string deviceId)
        {
            await repositories.SyncDevices.Collection.UpdateOneAsync(DeviceFilter(userId, deviceId), Builders<SyncDevice>.Update
                .Set(x => x.IsSyncReset, false));
            await repositories.DeviceIdsChunks.Collection.DeleteManyAsync(DeviceIdsChunkFilter(userId, deviceId, "pending"));
        }

        public async Task<SyncDevice?> GetDeviceAsync(string userId, string deviceId)
        {
            return await repositories.SyncDevices.Collection.Find(DeviceFilter(userId, deviceId)).FirstOrDefaultAsync();
        }

        public async Task ResetDevicesAsync(string userId)
        {
            await repositories.SyncDevices.Collection.DeleteManyAsync(UserFilter(userId));
            await repositories.DeviceIdsChunks.Collection.DeleteManyAsync(DeviceIdsChunkFilter(userId));
        }

        public async Task UpdateLastAccessTimeAsync(string userId, string deviceId)
        {
            await repositories.SyncDevices.Collection.UpdateOneAsync(DeviceFilter(userId, deviceId), Builders<SyncDevice>.Update
                .Set(x => x.LastAccessTime, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        public async Task AddIdsToOtherDevicesAsync(string userId, string deviceId, IEnumerable<ItemKey> ids)
        {
            await UpdateLastAccessTimeAsync(userId, deviceId);
            using var session = await repositories.DeviceIdsChunks.Collection.Database.Client.StartSessionAsync();
            await AddIdsToDevicesAsync(session, userId, deviceId, ids);
        }

        /// <summary>
        /// Queues adding the ids to the other devices in the current unit of work, so they
        /// are recorded in the same transaction as the items they refer to.
        /// </summary>
        public void QueueAddIdsToOtherDevices(string userId, string deviceId, IEnumerable<ItemKey> ids)
        {
            dbContext.AddCommand((session, ct) => AddIdsToDevicesAsync(session, userId, deviceId, ids, ct));
        }

        public async Task AddIdsToAllDevicesAsync(string userId, IEnumerable<ItemKey> ids)
        {
            using var session = await repositories.DeviceIdsChunks.Collection.Database.Client.StartSessionAsync();
            await AddIdsToDevicesAsync(session, userId, null, ids);
        }

        private async Task AddIdsToDevicesAsync(IClientSessionHandle session, string userId, string? excludedDeviceId, IEnumerable<ItemKey> ids, CancellationToken ct = default)
        {
            var devices = await repositories.SyncDevices.Collection.Find(session, UserFilter(userId)).ToListAsync(ct);
            foreach (var device in devices)
            {
                if (device.DeviceId == excludedDeviceId) continue;
                await AppendIdsAsync(session, userId, device.DeviceId, "unsynced", ids, ct);
            }
        }

        public async Task<SyncDevice> RegisterDeviceAsync(string userId, string deviceId)
        {
            var newDevice = new SyncDevice
            {
                UserId = userId,
                DeviceId = deviceId,
                LastAccessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsSyncReset = true
            };
            await repositories.SyncDevices.Collection.InsertOneAsync(newDevice);
            return newDevice;
        }

        public async Task UnregisterDeviceAsync(string userId, string deviceId)
        {
            await repositories.SyncDevices.Collection.DeleteOneAsync(DeviceFilter(userId, deviceId));
            await repositories.DeviceIdsChunks.Collection.DeleteManyAsync(DeviceIdsChunkFilter(userId, deviceId));
        }
    }
}