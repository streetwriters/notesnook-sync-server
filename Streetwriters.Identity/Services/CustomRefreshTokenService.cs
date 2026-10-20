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
using System.Threading.Tasks;
using IdentityServer4;
using IdentityServer4.Models;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;

namespace Streetwriters.Identity.Services
{
    public class CustomRefreshTokenService : DefaultRefreshTokenService
    {
        private readonly CustomPersistedGrantDbContext persistedGrants;

        public CustomRefreshTokenService(IRefreshTokenStore refreshTokenStore, IProfileService profile, ISystemClock clock, ILogger<DefaultRefreshTokenService> logger, CustomPersistedGrantDbContext persistedGrants) : base(refreshTokenStore, profile, clock, logger)
        {
            this.persistedGrants = persistedGrants;
        }

        public override async Task<string> UpdateRefreshTokenAsync(string handle, RefreshToken refreshToken, Client client)
        {
            var key = GetHashedKey(handle);

            // using this token proves the client received it, so the token it
            // replaced and any other tokens that replaced it (from concurrent refreshes
            // or lost responses) are no longer needed and must stop working
            var replacedKey = await persistedGrants.GetReplacedKeyAsync(key);
            if (replacedKey != null) await persistedGrants.RemoveReplacedAsync(replacedKey, key);

            var newHandle = await base.UpdateRefreshTokenAsync(handle, refreshToken, client);
            if (newHandle != handle) await persistedGrants.SetReplacedKeyAsync(GetHashedKey(newHandle), key);
            return newHandle;
        }

        protected override Task<bool> AcceptConsumedTokenAsync(RefreshToken refreshToken)
        {
            // A consumed token only exists until the token that replaced it is used
            // (see UpdateRefreshTokenAsync), so clients that refresh concurrently or
            // lose the refresh response can still use it.
            return Task.FromResult(true);
        }

        private static string GetHashedKey(string handle)
        {
            // same key IdentityServer's grant store uses
            return HashExtensions.Sha256(handle + ":" + IdentityServerConstants.PersistedGrantTypes.RefreshToken);
        }
    }
}