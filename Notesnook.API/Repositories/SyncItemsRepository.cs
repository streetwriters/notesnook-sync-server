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
using Microsoft.VisualBasic;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Repositories
{
    public class SyncItemsRepository<T> : Repository<SyncItem> where T : SyncItem
    {
        public SyncItemsRepository(IDbContext dbContext, string databaseName, string collectionName) : base(dbContext, databaseName, collectionName)
        {
            Collection.Indexes.CreateOne(new CreateIndexModel<SyncItem>(Builders<SyncItem>.IndexKeys.Ascending(i => i.UserId).Descending(i => i.DateSynced)));
            Collection.Indexes.CreateOne(new CreateIndexModel<SyncItem>(Builders<SyncItem>.IndexKeys.Ascending(i => i.UserId).Ascending((i) => i.ItemId)));
            Collection.Indexes.CreateOne(new CreateIndexModel<SyncItem>(Builders<SyncItem>.IndexKeys.Ascending(i => i.UserId)));
        }

        private readonly List<string> ALGORITHMS = new List<string> { Algorithms.Default };
        private bool IsValidAlgorithm(string algorithm)
        {
            return ALGORITHMS.Contains(algorithm);
        }

        public Task<long> CountItemsSyncedAfterAsync(string userId, long timestamp)
        {
            return Collection.CountDocumentsAsync(n => (n.DateSynced > timestamp) && n.UserId.Equals(userId));
        }
        public Task<IAsyncCursor<SyncItem>> FindItemsSyncedAfter(string userId, long timestamp, int batchSize)
        {
            return Collection.FindAsync(n => (n.DateSynced > timestamp) && n.UserId.Equals(userId), new FindOptions<SyncItem>
            {
                BatchSize = batchSize,
                AllowDiskUse = true,
                AllowPartialResults = false,
                NoCursorTimeout = true,
                Sort = new SortDefinitionBuilder<SyncItem>().Ascending((a) => a.Id)
            });
        }
        // public async Task DeleteIdsAsync(string[] ids, string userId, CancellationToken token = default(CancellationToken))
        // {
        //     await Collection.DeleteManyAsync<T>((i) => ids.Contains(i.Id) && i.UserId == userId, token);
        // }

        public void DeleteByUserId(string userId)
        {
            dbContext.AddCommand((handle, ct) => Collection.DeleteManyAsync(handle, (i) => i.UserId == userId, cancellationToken: ct));
        }

        public async Task UpsertAsync(SyncItem item, string userId, long dateSynced)
        {

            if (item.Length > 15 * 1024 * 1024)
            {
                throw new Exception($"Size of item \"{item.ItemId}\" is too large. Maximum allowed size is 15 MB.");
            }

            if (!IsValidAlgorithm(item.Algorithm))
            {
                throw new Exception($"Invalid alg identifier {item.Algorithm}");
            }

            item.DateSynced = dateSynced;
            item.UserId = userId;

            await base.UpsertAsync(item, (x) => (x.ItemId == item.ItemId) && x.UserId == userId);
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

            item.DateSynced = dateSynced;
            item.UserId = userId;

            // await base.UpsertAsync(item, (x) => (x.ItemId == item.ItemId) && x.UserId == userId);
            base.Upsert(item, (x) => (x.ItemId == item.ItemId) && x.UserId == userId);
        }
    }
}