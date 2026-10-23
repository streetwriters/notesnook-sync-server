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
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using Streetwriters.Common;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Streetwriters.Identity.Legacy
{
    /// <summary>
    /// Copies the tokens of every session OpenIddict starts into IdentityServer4's grants, as IdentityServer4
    /// would have stored them, so switching back to IdentityServer4 doesn't end them. Only needed while
    /// switching back is an option.
    /// </summary>
    public class RollbackBridge(IMongoDatabase database)
    {
        private IMongoCollection<BsonDocument> Grants => database.GetCollection<BsonDocument>("PersistedGrants");

        /// <returns>The key of the copy.</returns>
        public async Task<string> CopyAccessTokenAsync(string token, ClaimsPrincipal principal, string jti)
        {
            var key = LegacyGrants.KeyOf(token, LegacyGrants.ReferenceTokenType);
            var (created, lifetime) = TimesOf(principal);
            await InsertAsync(key, LegacyGrants.ReferenceTokenType, principal, created, lifetime, JsonSerializer.Serialize(AccessTokenData(principal, jti)), null);
            return key;
        }

        /// <param name="accessToken">The access token issued with it, which IdentityServer4 kept in the refresh token.</param>
        /// <param name="replacedKey">The key of the copy, or the legacy grant, of the refresh token this one replaced.</param>
        /// <returns>The key of the copy.</returns>
        public async Task<string> CopyRefreshTokenAsync(string token, ClaimsPrincipal principal, ClaimsPrincipal accessToken, string jti, string? replacedKey)
        {
            var key = LegacyGrants.KeyOf(token, LegacyGrants.RefreshTokenType);
            var (created, lifetime) = TimesOf(principal);
            var data = new Dictionary<string, object?>
            {
                ["CreationTime"] = created,
                ["Lifetime"] = lifetime,
                ["ConsumedTime"] = null,
                ["AccessToken"] = AccessTokenData(accessToken, jti),
                ["Version"] = 4,
            };
            await InsertAsync(key, LegacyGrants.RefreshTokenType, principal, created, lifetime, JsonSerializer.Serialize(data), replacedKey);
            return key;
        }

        private Task InsertAsync(string key, string type, ClaimsPrincipal principal, DateTime created, int lifetime, string data, string? replacedKey)
        {
            var document = new BsonDocument
            {
                ["Key"] = key,
                ["Type"] = type,
                ["SubjectId"] = principal.GetClaim(Claims.Subject),
                ["SessionId"] = BsonNull.Value,
                ["ClientId"] = principal.GetPresenters().First(),
                ["Description"] = BsonNull.Value,
                ["CreationTime"] = created,
                ["Expiration"] = created.AddSeconds(lifetime),
                ["ConsumedTime"] = BsonNull.Value,
                ["Data"] = data,
            };
            if (replacedKey != null) document["ReplacedKey"] = replacedKey;
            return Grants.ReplaceOneAsync(new BsonDocument("Key", key), document, new ReplaceOptions { IsUpsert = true });
        }

        /// <summary>An access token as IdentityServer4 serialized it: its reference tokens, and the one in each refresh token.</summary>
        private static Dictionary<string, object?> AccessTokenData(ClaimsPrincipal principal, string jti)
        {
            var (created, lifetime) = TimesOf(principal);
            var clientId = principal.GetPresenters().First();
            var claims = new List<object>
            {
                Claim(Claims.ClientId, clientId),
            };
            claims.AddRange(principal.GetScopes().Select((scope) => Claim(Claims.Scope, scope)));
            // the session's own claims: sub, auth_time, idp, amr, roles and the user's claims
            claims.AddRange(principal.Claims.Where((c) => !c.Type.StartsWith("oi_") && c.Type is not (Claims.ClientId or Claims.Scope or Claims.JwtId or Claims.IssuedAt))
                .Select((c) => Claim(c.Type, c.Value, c.ValueType)));
            claims.Add(Claim(Claims.JwtId, jti));
            claims.Add(Claim(Claims.IssuedAt, new DateTimeOffset(created).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));

            return new Dictionary<string, object?>
            {
                ["AllowedSigningAlgorithms"] = Array.Empty<string>(),
                ["Confirmation"] = null,
                ["Audiences"] = new[] { "notesnook" },
                ["Issuer"] = Servers.IdentityServer.ToString(),
                ["CreationTime"] = created,
                ["Lifetime"] = lifetime,
                ["Type"] = "access_token",
                ["ClientId"] = clientId,
                // reference
                ["AccessTokenType"] = 1,
                ["Description"] = null,
                ["Claims"] = claims,
                ["Version"] = 4,
            };
        }

        private static object Claim(string type, string value, string valueType = ClaimValueTypes.String) =>
            new Dictionary<string, string> { ["Type"] = type, ["Value"] = value, ["ValueType"] = valueType };

        private static (DateTime Created, int Lifetime) TimesOf(ClaimsPrincipal principal)
        {
            var created = principal.GetCreationDate()!.Value;
            return (created.UtcDateTime, (int)(principal.GetExpirationDate()!.Value - created).TotalSeconds);
        }

        public async Task RemoveAsync(IEnumerable<string?> keys)
        {
            var present = keys.OfType<string>().ToList();
            if (present.Count > 0) await Grants.DeleteManyAsync(Builders<BsonDocument>.Filter.In("Key", present));
        }
    }
}
