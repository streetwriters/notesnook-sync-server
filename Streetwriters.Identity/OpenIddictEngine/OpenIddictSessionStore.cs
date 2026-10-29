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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Legacy;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>Sessions as OpenIddict keeps them: an authorization per session, holding all of its tokens.</summary>
    /// <remarks>Sessions IdentityServer4 started are ended where it stored them, as it ended them.</remarks>
    public class OpenIddictSessionStore(IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens, IOpenIddictApplicationManager applications, TokenRecords records, LegacyGrants legacyGrants, RollbackBridge bridge) : ISessionStore
    {
        public async Task EndSessionAsync(string clientId, string subjectId, string jti)
        {
            var record = await records.FindByJtiAsync(jti);
            if (record == null)
            {
                await legacyGrants.RemoveByJtiAsync(subjectId, clientId, jti);
                return;
            }
            if (record.Subject != subjectId) return;
            if (record.AuthorizationId != null)
            {
                await EndAsync([record.AuthorizationId]);
                return;
            }
            // tokens issued without a refresh token, such as the password reset's, belong to no session
            await RevokeTokenAsync(record.TokenId);
        }

        /// <summary>Revokes one token, and removes its copy for IdentityServer4.</summary>
        public async Task RevokeTokenAsync(string tokenId)
        {
            if (await tokens.FindByIdAsync(tokenId) is object token) await tokens.TryRevokeAsync(token);
            await bridge.RemoveAsync([(await records.FindAsync(tokenId))?.BridgeKey]);
        }

        public async Task<List<string>> EndSessionsAsync(string clientId, string subjectId, string? exceptJti, string? exceptRefreshToken)
        {
            var application = await applications.FindByClientIdAsync(clientId);
            if (application == null) return [];
            var applicationId = await applications.GetIdAsync(application);

            var except = new HashSet<string>();
            if (exceptJti != null && (await records.FindByJtiAsync(exceptJti))?.AuthorizationId is string jtiAuthorization)
                except.Add(jtiAuthorization);
            if (exceptRefreshToken != null && await tokens.FindByReferenceIdAsync(exceptRefreshToken) is object refreshToken
                && await tokens.GetAuthorizationIdAsync(refreshToken) is string refreshTokenAuthorization)
                except.Add(refreshTokenAuthorization);

            var ended = new List<string>();
            await foreach (var authorization in authorizations.FindAsync(subject: subjectId, client: applicationId, status: null, type: null, scopes: null))
            {
                var id = await authorizations.GetIdAsync(authorization);
                if (id != null && !except.Contains(id)) ended.Add(id);
            }
            return [.. await EndAsync(ended), .. await legacyGrants.RemoveSessionsAsync(subjectId, clientId, exceptJti, exceptRefreshToken)];
        }

        public async Task<List<string>> EndAllSessionsAsync(string subjectId)
        {
            var ended = new List<string>();
            await foreach (var authorization in authorizations.FindBySubjectAsync(subjectId))
            {
                if (await authorizations.GetIdAsync(authorization) is string id) ended.Add(id);
            }
            return [.. await EndAsync(ended), .. await legacyGrants.RemoveAllAsync(subjectId)];
        }

        /// <returns>The keys under which introspection results of the sessions' access tokens are cached.</returns>
        private async Task<List<string>> EndAsync(List<string> authorizationIds)
        {
            foreach (var id in authorizationIds)
            {
                await tokens.RevokeByAuthorizationIdAsync(id);
                if (await authorizations.FindByIdAsync(id) is object authorization) await authorizations.TryRevokeAsync(authorization);
            }
            var revoked = await records.FindByAuthorizationsAsync(authorizationIds);
            await bridge.RemoveAsync(revoked.Select((r) => r.BridgeKey));
            // a session that was refreshed from a legacy one ends that one too
            foreach (var key in revoked.Select((r) => LegacyGrants.LineageKey(r.Replaced)).OfType<string>())
                await legacyGrants.RemoveAsync(key);
            return [.. revoked.Where((r) => r.CacheKey != null).Select((r) => r.CacheKey!)];
        }
    }
}
