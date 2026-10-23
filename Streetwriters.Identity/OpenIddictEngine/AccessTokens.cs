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
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Legacy;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>Validates session access tokens, with the claims IdentityServer4 gave them.</summary>
    public class AccessTokens(OpenIddictValidationService validation, TokenRecords records, LegacyGrants legacyGrants, UserManager<User> userManager)
    {
        /// <returns>The token's claims, or null if it isn't an active access token of a user that still exists.</returns>
        public async Task<ClaimsPrincipal?> ValidateAsync(string token)
        {
            var principal = LegacyGrants.IsLegacyHandle(token) ? await legacyGrants.ValidateAccessTokenAsync(token) : await ValidateOpenIddictTokenAsync(token);
            if (principal == null) return null;

            var subject = principal.GetClaim(Claims.Subject);
            // deleted users must not be able to use or introspect their tokens
            if (subject == null || await userManager.FindByIdAsync(subject) == null) return null;

            var record = principal.GetTokenId() is string tokenId ? await records.FindAsync(tokenId) : null;
            var identity = (ClaimsIdentity)principal.Identity!;
            if (record?.Jti != null) identity.SetClaim(Claims.JwtId, record.Jti);
            if (principal.GetPresenters().FirstOrDefault() is string clientId) identity.SetClaim(Claims.ClientId, clientId);
            foreach (var scope in principal.GetScopes()) identity.AddClaim(new Claim(Claims.Scope, scope));
            return principal;
        }

        private async Task<ClaimsPrincipal?> ValidateOpenIddictTokenAsync(string token)
        {
            try
            {
                return await validation.ValidateAccessTokenAsync(token);
            }
            catch (OpenIddictExceptions.ProtocolException)
            {
                return null;
            }
        }
    }
}
