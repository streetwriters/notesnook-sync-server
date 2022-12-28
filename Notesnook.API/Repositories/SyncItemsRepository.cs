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
    public class SyncItemsRepository<T> : Repository<T> where T : SyncItem
    {
        public SyncItemsRepository(IDbContext dbContext) : base(dbContext)
        {
            Collection.Indexes.CreateOne(new CreateIndexModel<T>(Builders<T>.IndexKeys.Descending(i => i.DateSynced).Ascending(i => i.UserId)));
            Collection.Indexes.CreateOne(new CreateIndexModel<T>(Builders<T>.IndexKeys.Ascending((i) => i.ItemId).Ascending(i => i.UserId)));
        }

        private readonly List<string> ALGORITHMS = new List<string> { Algorithms.Default };
        private bool IsValidAlgorithm(string algorithm)
        {
            return ALGORITHMS.Contains(algorithm);
        }

        public async Task<IEnumerable<T>> GetItemsSyncedAfterAsync(string userId, long timestamp)
        {
            var cursor = await Collection.FindAsync(n => (n.DateSynced > timestamp) && n.UserId.Equals(userId));
            return cursor.ToList();
        }

        // public async Task DeleteIdsAsync(string[] ids, string userId, CancellationToken token = default(CancellationToken))
        // {
        //     await Collection.DeleteManyAsync<T>((i) => ids.Contains(i.Id) && i.UserId == userId, token);
        // }

        public void DeleteByUserId(string userId)
        {
            dbContext.AddCommand((handle, ct) => Collection.DeleteManyAsync<T>(handle, (i) => i.UserId == userId, cancellationToken: ct));
        }

        public async Task UpsertAsync(T item, string userId, long dateSynced)
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

        public void Upsert(T item, string userId, long dateSynced)
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