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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Notesnook.API.Services
{
    // Runs in the background because building an index on a large collection can take long enough to fail health checks.
    public class MongoIndexesHostedService(IMongoClient client, ILogger<MongoIndexesHostedService> logger) : BackgroundService
    {
        private readonly record struct Index(string Name, BsonDocument Keys, bool Unique = false, bool Background = false);

        private static readonly Index[] SyncIndexes =
        [
            I("UserId_1__id_1", ("UserId", 1), ("_id", 1)),
            I("UserId_1_ItemId_1", true, true, ("UserId", 1), ("ItemId", 1))
        ];

        private static readonly Dictionary<string, Index[]> Indexes = new()
        {
            ["announcements"] = [I("IsActive_1", ("IsActive", 1)), I("UserIds_1", ("UserIds", 1))],
            ["attachments"] = SyncIndexes, ["colors"] = SyncIndexes, ["content"] = SyncIndexes,
            ["notebooks"] = SyncIndexes, ["relations"] = SyncIndexes, ["reminders"] = SyncIndexes,
            ["settings"] = SyncIndexes, ["settingsv2"] = SyncIndexes, ["shortcuts"] = SyncIndexes,
            ["tags"] = SyncIndexes, ["vaults"] = SyncIndexes,
            ["device_ids_chunks"] = [I("UserId_1_DeviceId_1_Key_1", ("UserId", 1), ("DeviceId", 1), ("Key", 1)), I("DeviceId_1", ("DeviceId", 1))],
            ["inbox_api_keys"] = [I("Key_1", ("Key", 1)), I("UserId_1", ("UserId", 1))],
            ["inbox_items"] = [I("UserId_1_ItemId_1", ("UserId", 1), ("ItemId", 1))],
            ["inbox_items_history"] = [I("UserId_1_ItemId_1", true, true, ("UserId", 1), ("ItemId", 1))],
            ["monographs"] = [I("ItemId_1", ("ItemId", 1)), I("UserId_1", ("UserId", 1)), I("Slug_1", ("Slug", 1)), I("_id_1_ItemId_1", ("_id", 1), ("ItemId", 1))],
            ["notes"] = [.. SyncIndexes, I("Length_1", ("Length", 1))],
            ["sync_devices"] = [I("UserId_1_DeviceId_1", true, false, ("UserId", 1), ("DeviceId", 1)), I("LastAccessTime_1", ("LastAccessTime", 1)),
                I("UserId_1", ("UserId", 1)), I("idx_device_user_activity", ("UserId", 1), ("LastAccessTime", -1))],
            ["user_settings"] = [I("UserId_1", true, false, ("UserId", 1)), I("DeletedAt_1", ("DeletedAt", 1)), I("PurgePending_1", ("PurgePending", 1))]
        };

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await EnsureIndexesAsync(client.GetDatabase("notesnook"), stoppingToken);
            }
            // an exception escaping a BackgroundService stops the whole host
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to ensure MongoDB indexes.");
            }
        }

        public async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
        {
            foreach (var (collectionName, expected) in Indexes)
            {
                var collection = database.GetCollection<BsonDocument>(collectionName);
                var cursor = await collection.Indexes.ListAsync(cancellationToken);
                var existing = await cursor.ToListAsync(cancellationToken);

                foreach (var index in expected.Where(index => !existing.Any(current => Matches(current, index))))
                {
                    if (existing.Any(current => current.GetValue("name", "") == index.Name))
                    {
                        logger.LogError("Index {Index} on {Collection} has an unexpected definition.", index.Name, collectionName);
                        continue;
                    }
                    try
                    {
                        await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(index.Keys, new CreateIndexOptions
                        {
                            Name = index.Name, Unique = index.Unique, Background = index.Background
                        }), cancellationToken: cancellationToken);
                    }
                    // e.g. a unique index over existing duplicates; the other indexes are still created
                    catch (MongoCommandException ex)
                    {
                        logger.LogError(ex, "Failed to create index {Index} on {Collection}.", index.Name, collectionName);
                    }
                }
            }
        }

        private static bool Matches(BsonDocument current, Index index) =>
            current.GetValue("key", new BsonDocument()).ToBsonDocument().Equals(index.Keys)
            && current.GetValue("unique", false).ToBoolean() == index.Unique
            && !current.Contains("partialFilterExpression");

        private static Index I(string name, params (string Field, int Direction)[] keys) => I(name, false, false, keys);
        private static Index I(string name, bool unique, bool background, params (string Field, int Direction)[] keys) =>
            new(name, new BsonDocument(keys.Select(key => new BsonElement(key.Field, key.Direction))), unique, background);
    }
}
