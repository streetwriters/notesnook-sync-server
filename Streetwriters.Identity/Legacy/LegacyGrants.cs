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
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenIddict.Abstractions;

namespace Streetwriters.Identity.Legacy
{
    /// <summary>A session token IdentityServer4 issued, as it stored it.</summary>
    public record LegacyGrant(string Key, string Type, string SubjectId, string ClientId, DateTime CreationTime, int Lifetime, IReadOnlyList<Claim> Claims, string? ReplacedKey)
    {
        public DateTime Expiration => CreationTime.AddSeconds(Lifetime);
    }

    /// <summary>
    /// The sessions IdentityServer4 started, which must keep working until they expire (at most 45 days after
    /// the switch). Reads and ends them where IdentityServer4 stored them, without IdentityServer4.
    /// </summary>
    public partial class LegacyGrants(IMongoDatabase database, TimeProvider timeProvider)
    {
        public const string RefreshTokenType = "refresh_token";
        public const string ReferenceTokenType = "reference_token";

        /// <summary>Prefix of a token record's Replaced when the token it replaced is a legacy refresh token.</summary>
        public const string LineagePrefix = "legacy:";

        public static string? LineageKey(string? replaced) => replaced?.StartsWith(LineagePrefix) == true ? replaced[LineagePrefix.Length..] : null;

        private static readonly Meter Meter = new("Streetwriters.Identity.Legacy");
        private static readonly Counter<long> Hits = Meter.CreateCounter<long>("identity_legacy_token_hits", description: "Legacy (IdentityServer4) tokens used, by type and outcome");

        private IMongoCollection<BsonDocument> Grants => database.GetCollection<BsonDocument>("PersistedGrants");

        /// <summary>IdentityServer4's handles: 32 random bytes in uppercase hex. OpenIddict's never look like this.</summary>
        public static bool IsLegacyHandle(string? token) => token != null && Handle().IsMatch(token);

        public static string KeyOf(string handle, string type) => (handle + ":" + type).Sha256();

        public async Task<LegacyGrant?> FindAsync(string handle, string type)
        {
            if (!IsLegacyHandle(handle)) return null;
            var document = await Grants.Find(new BsonDocument("Key", KeyOf(handle, type))).FirstOrDefaultAsync();
            var grant = document == null ? null : Parse(document);
            Hits.Add(1, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("outcome", grant == null ? "missing" : IsExpired(grant) ? "expired" : "valid"));
            return grant;
        }

        public bool IsExpired(LegacyGrant grant) => grant.Expiration <= timeProvider.GetUtcNow().UtcDateTime;

        /// <summary>A reference access token's claims, in the form OpenIddict gives its own; null unless it's valid.</summary>
        public async Task<ClaimsPrincipal?> ValidateAccessTokenAsync(string token)
        {
            var grant = await FindAsync(token, ReferenceTokenType);
            if (grant == null || IsExpired(grant)) return null;
            return ToPrincipal(grant);
        }

        public static ClaimsPrincipal ToPrincipal(LegacyGrant grant)
        {
            var identity = new ClaimsIdentity("legacy", OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);
            identity.AddClaims(grant.Claims.Where((c) => c.Type is not (OpenIddictConstants.Claims.Scope or OpenIddictConstants.Claims.IssuedAt)));
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(grant.Claims.Where((c) => c.Type == OpenIddictConstants.Claims.Scope).Select((c) => c.Value));
            principal.SetPresenters(grant.ClientId);
            principal.SetCreationDate(new DateTimeOffset(grant.CreationTime));
            principal.SetExpirationDate(new DateTimeOffset(grant.Expiration));
            return principal;
        }

        /// <summary>As IdentityServer4 marked a refresh token it replaced, which it went on accepting.</summary>
        public Task MarkConsumedAsync(string key) =>
            Grants.UpdateOneAsync(new BsonDocument("Key", key), Builders<BsonDocument>.Update.Set("ConsumedTime", timeProvider.GetUtcNow().UtcDateTime));

        /// <summary>The refresh tokens IdentityServer4 issued in place of the one with <paramref name="replacedKey"/>.</summary>
        public async Task<List<string>> FindReplacementsAsync(string replacedKey) =>
            (await Grants.Find(new BsonDocument("ReplacedKey", replacedKey)).Project(Builders<BsonDocument>.Projection.Include("Key")).ToListAsync())
                .Select((d) => d["Key"].AsString).ToList();

        public Task RemoveAsync(string key) => Grants.DeleteOneAsync(new BsonDocument("Key", key));

        /// <summary>Removes the session of the access token with this jti, as IdentityServer4's logout matched it.</summary>
        public Task RemoveByJtiAsync(string subjectId, string clientId, string jti) =>
            Grants.DeleteManyAsync(Builders<BsonDocument>.Filter.And(SubjectAndClient(subjectId, clientId), Builders<BsonDocument>.Filter.Regex("Data", new BsonRegularExpression(Regex.Escape(jti)))));

        /// <returns>The removed grants' keys, which for access tokens are also the keys the API servers cache their introspection under.</returns>
        public async Task<List<string>> RemoveSessionsAsync(string subjectId, string clientId, string? exceptJti, string? exceptRefreshToken)
        {
            var exceptKey = exceptRefreshToken != null ? KeyOf(exceptRefreshToken, RefreshTokenType) : null;
            var removed = (await Grants.Find(SubjectAndClient(subjectId, clientId)).ToListAsync())
                .Where((d) => !(exceptJti != null && d["Data"].AsString.Contains(exceptJti)) && d["Key"].AsString != exceptKey)
                .Select((d) => d["Key"].AsString)
                .ToList();
            if (removed.Count > 0) await Grants.DeleteManyAsync(Builders<BsonDocument>.Filter.In("Key", removed));
            return removed;
        }

        /// <summary>Removes the user's access tokens on the client, as IdentityServer4 did when a refresh token was revoked.</summary>
        public Task RemoveAccessTokensAsync(string subjectId, string clientId) =>
            Grants.DeleteManyAsync(Builders<BsonDocument>.Filter.And(SubjectAndClient(subjectId, clientId), Builders<BsonDocument>.Filter.Eq("Type", ReferenceTokenType)));

        public async Task<List<string>> RemoveAllAsync(string subjectId)
        {
            var filter = new BsonDocument("SubjectId", subjectId);
            var removed = (await Grants.Find(filter).Project(Builders<BsonDocument>.Projection.Include("Key")).ToListAsync())
                .Select((d) => d["Key"].AsString).ToList();
            await Grants.DeleteManyAsync(filter);
            return removed;
        }

        public async Task<long> RemoveExpiredAsync() =>
            (await Grants.DeleteManyAsync(Builders<BsonDocument>.Filter.Lt("Expiration", timeProvider.GetUtcNow().UtcDateTime))).DeletedCount;

        private static FilterDefinition<BsonDocument> SubjectAndClient(string subjectId, string clientId) =>
            Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("SubjectId", subjectId), Builders<BsonDocument>.Filter.Eq("ClientId", clientId));

        private static LegacyGrant Parse(BsonDocument document)
        {
            var type = document["Type"].AsString;
            using var data = JsonDocument.Parse(document["Data"].AsString);
            // a refresh token holds the access token it was issued with, and with it the session's claims
            var token = type == RefreshTokenType ? data.RootElement.GetProperty("AccessToken") : data.RootElement;
            var claims = token.GetProperty("Claims").EnumerateArray().Select((c) => new Claim(
                c.GetProperty("Type").GetString()!,
                c.GetProperty("Value").GetString()!,
                c.TryGetProperty("ValueType", out var valueType) ? valueType.GetString() : null)).ToList();
            return new LegacyGrant(
                document["Key"].AsString,
                type,
                document["SubjectId"].AsString,
                document["ClientId"].AsString,
                DateTime.Parse(data.RootElement.GetProperty("CreationTime").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                data.RootElement.GetProperty("Lifetime").GetInt32(),
                claims,
                document.TryGetValue("ReplacedKey", out var replaced) && replaced.IsString ? replaced.AsString : null);
        }

        [GeneratedRegex("^[0-9A-F]{64}$")]
        private static partial Regex Handle();
    }
}
