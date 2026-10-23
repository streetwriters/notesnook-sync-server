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
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// What IdentityServer4 knew about each token that OpenIddict doesn't: the jti clients and the other
    /// servers identify an access token by, the key the other servers cache its introspection under, and
    /// the refresh token each refresh token replaced.
    /// </summary>
    public class TokenRecord
    {
        [BsonId] public required string TokenId { get; set; }
        public required string Type { get; set; }
        public required string Subject { get; set; }
        public string? AuthorizationId { get; set; }
        public string? Jti { get; set; }
        public string? CacheKey { get; set; }
        public string? Replaced { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class TokenRecords
    {
        public const string AccessToken = "access_token";
        public const string RefreshToken = "refresh_token";

        private readonly IMongoCollection<TokenRecord> records;

        public TokenRecords(IMongoDatabase database)
        {
            records = database.GetCollection<TokenRecord>("openiddict.token_records");
            var keys = Builders<TokenRecord>.IndexKeys;
            records.Indexes.CreateMany([
                new CreateIndexModel<TokenRecord>(keys.Ascending((r) => r.Jti), new CreateIndexOptions { Sparse = true }),
                new CreateIndexModel<TokenRecord>(keys.Ascending((r) => r.AuthorizationId)),
                new CreateIndexModel<TokenRecord>(keys.Ascending((r) => r.Replaced), new CreateIndexOptions { Sparse = true }),
                // records outlive their tokens by a day, so they never go missing while a token can still be used
                new CreateIndexModel<TokenRecord>(keys.Ascending((r) => r.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(1) }),
            ]);
        }

        public Task AddAsync(TokenRecord record) => records.ReplaceOneAsync((r) => r.TokenId == record.TokenId, record, new ReplaceOptions { IsUpsert = true });

        public async Task<TokenRecord?> FindAsync(string tokenId) => await records.Find((r) => r.TokenId == tokenId).FirstOrDefaultAsync();

        public async Task<TokenRecord?> FindByJtiAsync(string jti) => await records.Find((r) => r.Jti == jti).FirstOrDefaultAsync();

        /// <summary>The refresh tokens that replaced <paramref name="tokenId"/>.</summary>
        public Task<List<TokenRecord>> FindReplacementsAsync(string tokenId) => records.Find((r) => r.Replaced == tokenId).ToListAsync();

        public Task<List<TokenRecord>> FindByAuthorizationsAsync(IEnumerable<string> authorizationIds) =>
            records.Find(Builders<TokenRecord>.Filter.In((r) => r.AuthorizationId, authorizationIds)).ToListAsync();
    }
}
