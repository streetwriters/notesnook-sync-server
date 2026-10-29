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
using System.Threading.Tasks;
using IdentityServer4;
using IdentityServer4.Stores;
using Streetwriters.Identity.Interfaces;

namespace Streetwriters.Identity.Services
{
    /// <summary>
    /// Sessions as IdentityServer4 stores them: a grant per token, with the session's tokens
    /// recognised by the access token's jti in their data.
    /// </summary>
    public class PersistedGrantSessionStore(IPersistedGrantStore persistedGrantStore) : ISessionStore
    {
        public async Task EndSessionAsync(string clientId, string subjectId, string jti)
        {
            var grants = await persistedGrantStore.GetAllAsync(new PersistedGrantFilter
            {
                ClientId = clientId,
                SubjectId = subjectId
            });
            foreach (var grant in grants.Where((grant) => grant.Data.Contains(jti)))
            {
                await persistedGrantStore.RemoveAsync(grant.Key);
            }
        }

        public async Task<List<string>> EndSessionsAsync(string clientId, string subjectId, string? exceptJti, string? exceptRefreshToken)
        {
            var grants = await persistedGrantStore.GetAllAsync(new PersistedGrantFilter
            {
                ClientId = clientId,
                SubjectId = subjectId
            });
            var refreshTokenKey = exceptRefreshToken != null ? (exceptRefreshToken + ":" + IdentityServerConstants.PersistedGrantTypes.RefreshToken).Sha256() : null;
            List<string> removedKeys = [];
            foreach (var grant in grants)
            {
                if ((exceptJti != null && grant.Data.Contains(exceptJti)) || grant.Key == refreshTokenKey) continue;
                await persistedGrantStore.RemoveAsync(grant.Key);
                // the grant's key is also the key its introspection result is cached under
                removedKeys.Add(grant.Key);
            }
            return removedKeys;
        }

        public async Task<List<string>> EndAllSessionsAsync(string subjectId)
        {
            var filter = new PersistedGrantFilter { SubjectId = subjectId };
            var keys = (await persistedGrantStore.GetAllAsync(filter)).Select((grant) => grant.Key).ToList();
            await persistedGrantStore.RemoveAllAsync(filter);
            return keys;
        }
    }
}
