// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using IdentityServer4.MongoDB;
using IdentityServer4.MongoDB.Configuration;
using IdentityServer4.MongoDB.DbContexts;
using IdentityServer4.MongoDB.Entities;
using IdentityServer4.MongoDB.Interfaces;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Streetwriters.Identity.Services
{
    public class CustomPersistedGrantDbContext : MongoDBContextBase, IPersistedGrantDbContext
    {
        private readonly IMongoCollection<PersistedGrant> _persistedGrants;

        public CustomPersistedGrantDbContext(IOptions<MongoDBConfiguration> settings)
            : base(settings)
        {
            _persistedGrants = Database.GetCollection<PersistedGrant>(Constants.TableNames.PersistedGrant);
            CreateIndexes();
        }

        private void CreateIndexes()
        {
            var indexOptions = new CreateIndexOptions() { Background = true };
            var builder = Builders<PersistedGrant>.IndexKeys;

            var expirationIndexModel = new CreateIndexModel<PersistedGrant>(builder.Descending(_ => _.Expiration), indexOptions);
            var keyIndexModel = new CreateIndexModel<PersistedGrant>(builder.Ascending(_ => _.Key), indexOptions);
            var subIndexModel = new CreateIndexModel<PersistedGrant>(builder.Ascending(_ => _.SubjectId), indexOptions);
            var clientIdSubIndexModel = new CreateIndexModel<PersistedGrant>(
              builder.Combine(
                  builder.Ascending(_ => _.ClientId),
                  builder.Ascending(_ => _.SubjectId)),
              indexOptions);

            var clientIdSubTypeIndexModel = new CreateIndexModel<PersistedGrant>(
              builder.Combine(
                  builder.Ascending(_ => _.ClientId),
                  builder.Ascending(_ => _.SubjectId),
                  builder.Ascending(_ => _.Type)),
              indexOptions);

            _persistedGrants.Indexes.CreateOne(expirationIndexModel);
            _persistedGrants.Indexes.CreateOne(keyIndexModel);
            _persistedGrants.Indexes.CreateOne(subIndexModel);
            _persistedGrants.Indexes.CreateOne(clientIdSubIndexModel);
            _persistedGrants.Indexes.CreateOne(clientIdSubTypeIndexModel);

            var replacedKeyIndexModel = new CreateIndexModel<PersistedGrant>(builder.Ascending(ReplacedKeyField), new CreateIndexOptions() { Background = true, Sparse = true });
            _persistedGrants.Indexes.CreateOne(replacedKeyIndexModel);
        }

        public IQueryable<PersistedGrant> PersistedGrants
        {
            get { return _persistedGrants.AsQueryable(); }
        }

        public Task Remove(Expression<Func<PersistedGrant, bool>> filter)
        {
            return _persistedGrants.DeleteManyAsync(filter);
        }

        public Task RemoveExpired()
        {
            return Remove(x => x.Expiration.HasValue && x.Expiration.Value < DateTime.UtcNow);
        }

        public Task InsertOrUpdate(Expression<Func<PersistedGrant, bool>> filter, PersistedGrant entity)
        {
            return _persistedGrants.ReplaceOneAsync(filter, entity, new ReplaceOptions() { IsUpsert = true });
        }

        // IdentityServer has no field for the refresh token a refresh token replaced,
        // so it's stored alongside the grant
        private const string ReplacedKeyField = "ReplacedKey";

        public async Task<string?> GetReplacedKeyAsync(string key)
        {
            var grant = await Database.GetCollection<BsonDocument>(Constants.TableNames.PersistedGrant)
                .Find(Builders<BsonDocument>.Filter.Eq("Key", key))
                .Project(Builders<BsonDocument>.Projection.Include(ReplacedKeyField))
                .FirstOrDefaultAsync();
            return grant != null && grant.TryGetValue(ReplacedKeyField, out var replacedKey) && replacedKey.IsString ? replacedKey.AsString : null;
        }

        /// <summary>
        /// Removes the grant with <paramref name="replacedKey"/> and every other grant
        /// that replaced it except <paramref name="key"/>.
        /// </summary>
        public Task RemoveReplacedAsync(string replacedKey, string key)
        {
            var filter = Builders<PersistedGrant>.Filter;
            return _persistedGrants.DeleteManyAsync(filter.Or(
                filter.Eq((g) => g.Key, replacedKey),
                filter.And(filter.Eq(ReplacedKeyField, replacedKey), filter.Ne((g) => g.Key, key))));
        }

        public Task SetReplacedKeyAsync(string key, string replacedKey)
        {
            return _persistedGrants.UpdateOneAsync(Builders<PersistedGrant>.Filter.Eq((g) => g.Key, key), Builders<PersistedGrant>.Update.Set(ReplacedKeyField, replacedKey));
        }
    }
}