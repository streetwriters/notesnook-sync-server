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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel;
using Microsoft.VisualBasic;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Helpers;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Data.DbContexts;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;
using AspNetCore.Identity.Mongo.Mongo;

namespace Notesnook.API.Repositories
{
    public class SyncItemsRepository : Repository<SyncItem>
    {
        private readonly string collectionName;
        private readonly ILogger<SyncItemsRepository> logger;
        private readonly UserGenerations generations;
        public SyncItemsRepository(IDbContext dbContext, IMongoCollection<SyncItem> collection, UserGenerations generations, ILogger<SyncItemsRepository> logger) : base(dbContext, collection)
        {
            this.collectionName = collection.CollectionNamespace.CollectionName;
            this.logger = logger;
            this.generations = generations;
        }

        public async Task<IAsyncCursor<SyncItemSize>> FindItemSizes(string userId, IEnumerable<string> ids, bool all)
        {
            var filters = new List<FilterDefinition<SyncItem>>(new[] { Builders<SyncItem>.Filter.Eq("UserId", userId), UserGenerations.Filter(await generations.GetAsync(userId)) });

            if (!all) filters.Add(Builders<SyncItem>.Filter.In("ItemId", ids));

            // the size is computed by the server, so it can't be faked by the client's Length
            return await Collection.FindAsync(Builders<SyncItem>.Filter.And(filters), new FindOptions<SyncItem, SyncItemSize>
            {
                Projection = new BsonDocument { { "_id", 1 }, { "Size", new BsonDocument("$bsonSize", "$$ROOT") } },
                // less than a chunk ahead, so the second read of each item (to send it) is still in cache:
                // 10k read every item from storage twice under cache pressure, 250 about 1.2x, smaller no better
                BatchSize = 250,
                AllowDiskUse = true,
                AllowPartialResults = false,
                NoCursorTimeout = true
            });
        }

        public async Task<List<RawSyncItem>> FindItemsByObjectIds(string userId, IReadOnlyCollection<ObjectId> ids)
        {
            var filter = Builders<SyncItem>.Filter.Eq("UserId", userId) & UserGenerations.Filter(await generations.GetAsync(userId)) & Builders<SyncItem>.Filter.In("_id", ids);
            // one more than can match, so the server returns every item and closes the cursor in a single round-trip
            return await Collection.Find(filter, new FindOptions { BatchSize = ids.Count + 1 })
                .As<RawSyncItem>()
                .ToListAsync();
        }

        public Task DeleteOlderGenerationsAsync(string userId, long generation)
        {
            return Collection.DeleteManyAsync(Builders<SyncItem>.Filter.And(Builders<SyncItem>.Filter.Eq("UserId", userId), UserGenerations.OlderThan(generation)));
        }

        // pushed items are written as the BSON the server builds itself, so their ciphers never become strings
        private IMongoCollection<RawBsonDocument>? rawCollection;
        private IMongoCollection<RawBsonDocument> RawCollection => rawCollection ??=
            Collection.Database.GetCollection<RawBsonDocument>(Collection.CollectionNamespace.CollectionName, Collection.Settings);

        // the items must stay undisposed until the unit of work commits
        public void UpsertMany(IEnumerable<PushedSyncItem> items, string userId)
        {
            var pushed = items.ToList();
            foreach (var item in pushed) SyncItemValidator.Validate(item, collectionName, logger);
            if (pushed.Count == 0) return;

            var dateSynced = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            dbContext.AddCommand(async (handle, ct) =>
            {
                var generation = await generations.GetAsync(userId);
                // rebuilt on every attempt, since a retried transaction can see a newer generation
                var buffers = new List<byte[]>(pushed.Count);
                try
                {
                    // raw filters: typed ones would convert their values through the documents' serializer
                    var writes = pushed.Select((item) => new ReplaceOneModel<RawBsonDocument>(
                        new BsonDocument { { "UserId", userId }, { "ItemId", item.ItemId } },
                        item.ToRawBson(userId, generation, dateSynced, buffers))
                    { IsUpsert = true }).ToList();
                    await RawCollection.BulkWriteAsync(handle, writes, options: new BulkWriteOptions { IsOrdered = false }, ct);
                }
                finally
                {
                    foreach (var buffer in buffers) LargeBuffers.Return(buffer);
                }
            });
        }
    }
}