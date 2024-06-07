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
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Data.DbContexts;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Repositories
{
    public class SyncItemsRepository : Repository<SyncItem>
    {
        private readonly string collectionName;
        public SyncItemsRepository(IDbContext dbContext, IMongoCollection<SyncItem> collection) : base(dbContext, collection)
        {
            this.collectionName = collection.CollectionNamespace.CollectionName;
#if DEBUG
            Collection.Indexes.CreateMany([
                new CreateIndexModel<SyncItem>(Builders<SyncItem>.IndexKeys.Ascending("UserId").Descending("DateSynced")),
                new CreateIndexModel<SyncItem>(Builders<SyncItem>.IndexKeys.Ascending("UserId").Ascending("ItemId")),
                new CreateIndexModel<SyncItem>(Builders<SyncItem>.IndexKeys.Ascending("UserId"))
            ]);
#endif
        }

        private readonly List<string> ALGORITHMS = [Algorithms.Default];
        private bool IsValidAlgorithm(string algorithm)
        {
            return ALGORITHMS.Contains(algorithm);
        }

        public Task<long> CountItemsSyncedAfterAsync(string userId, long timestamp)
        {
            var filter = Builders<SyncItem>.Filter.And(Builders<SyncItem>.Filter.Gt("DateSynced", timestamp), Builders<SyncItem>.Filter.Eq("UserId", userId));
            return Collection.CountDocumentsAsync(filter);
        }
        public Task<IAsyncCursor<SyncItem>> FindItemsSyncedAfter(string userId, long timestamp, int batchSize)
        {
            var filter = Builders<SyncItem>.Filter.And(Builders<SyncItem>.Filter.Gt("DateSynced", timestamp), Builders<SyncItem>.Filter.Eq("UserId", userId));
            return Collection.FindAsync(filter, new FindOptions<SyncItem>
            {
                BatchSize = batchSize,
                AllowDiskUse = true,
                AllowPartialResults = false,
                NoCursorTimeout = true,
                Sort = new SortDefinitionBuilder<SyncItem>().Ascending("_id")
            });
        }

        public Task<IAsyncCursor<SyncItem>> FindItemsById(string userId, IEnumerable<string> ids, bool all, int batchSize)
        {
            var filters = new List<FilterDefinition<SyncItem>>(new[] { Builders<SyncItem>.Filter.Eq("UserId", userId) });

            if (!all) filters.Add(Builders<SyncItem>.Filter.In("ItemId", ids));

            return Collection.FindAsync(Builders<SyncItem>.Filter.And(filters), new FindOptions<SyncItem>
            {
                BatchSize = batchSize,
                AllowDiskUse = true,
                AllowPartialResults = false,
                NoCursorTimeout = true
            });
        }

        public void DeleteByUserId(string userId)
        {
            var filter = Builders<SyncItem>.Filter.Eq("UserId", userId);
            var writes = new List<WriteModel<SyncItem>>
            {
                new DeleteManyModel<SyncItem>(filter)
            };
            dbContext.AddCommand((handle, ct) => Collection.BulkWriteAsync(handle, writes, options: null, ct));
        }

        public void Upsert(SyncItem item, string userId, long dateSynced)
        {
            if (item.Length > 15 * 1024 * 1024)
            {
                throw new Exception($"Size of item \"{item.ItemId}\" is too large. Maximum allowed size is 15 MB.");
            }

            if (!IsValidAlgorithm(item.Algorithm))
            {
                throw new Exception($"Invalid alg identifier {item.Algorithm}");
            }

            // Handle case where the cipher is corrupted.
            if (!IsBase64String(item.Cipher))
            {
                Slogger<SyncHub>.Error("Upsert", "Corrupted", item.ItemId, item.Length.ToString(), item.Cipher);
                throw new Exception($"Corrupted item \"{item.ItemId}\" in collection \"{this.collectionName}\". Please report this error to support@streetwriters.co.");
            }

            item.DateSynced = dateSynced;
            item.UserId = userId;

            var filter = Builders<SyncItem>.Filter.And(
                Builders<SyncItem>.Filter.Eq("UserId", userId),
                Builders<SyncItem>.Filter.Eq("ItemId", item.ItemId)
            );

            dbContext.AddCommand((handle, ct) => Collection.ReplaceOneAsync(handle, filter, item, new ReplaceOptions { IsUpsert = true }, ct));
            // await base.UpsertAsync(item, (x) => (x.ItemId == item.ItemId) && x.UserId == userId);
            base.Upsert(item, (x) => x.UserId == userId && x.ItemId == item.ItemId);
        }

        private static bool IsBase64String(string value)
        {
            if (value == null || value.Length == 0 || value.Contains(' ') || value.Contains('\t') || value.Contains('\r') || value.Contains('\n'))
                return false;
            var index = value.Length - 1;
            if (value[index] == '=')
                index--;
            if (value[index] == '=')
                index--;
            for (var i = 0; i <= index; i++)
                if (IsInvalidBase64Char(value[i]))
                    return false;
            return true;
        }

        private static bool IsInvalidBase64Char(char value)
        {
            var code = (int)value;
            // 1 - 9
            if (code >= 48 && code <= 57)
                return false;
            // A - Z
            if (code >= 65 && code <= 90)
                return false;
            // a - z
            if (code >= 97 && code <= 122)
                return false;
            // - & _
            return code != 45 && code != 95;
        }
    }
}