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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Streetwriters.Common.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>The identity a session's tokens carry: the user's roles and claims, as IdentityServer4's ProfileService gave them.</summary>
    public class SessionPrincipals(UserManager<User> userManager, TimeProvider timeProvider)
    {
        public const string AuthenticationTimeClaim = "auth_time";
        public const string AuthenticationMethodClaim = "amr";

        public async Task<ClaimsPrincipal> CreateAsync(User user, IEnumerable<string> scopes, IEnumerable<string> authenticationMethods, long? authenticationTime = null)
        {
            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, user.Id.ToString());
            identity.SetClaim("idp", "local");
            identity.AddClaim(new Claim(AuthenticationTimeClaim, (authenticationTime ?? timeProvider.GetUtcNow().ToUnixTimeSeconds()).ToString(), ClaimValueTypes.Integer64));
            foreach (var method in authenticationMethods) identity.AddClaim(new Claim(AuthenticationMethodClaim, method));
            foreach (var role in await userManager.GetRolesAsync(user)) identity.AddClaim(new Claim(Claims.Role, role));
            foreach (var claim in user.Claims) identity.AddClaim(claim.ToClaim());

            identity.SetScopes(scopes);
            identity.SetDestinations((_) => [Destinations.AccessToken]);
            return new ClaimsPrincipal(identity);
        }

        /// <summary>The identity for a refreshed session: the user's current roles and claims, with how and when they signed in.</summary>
        public async Task<ClaimsPrincipal> RefreshAsync(User user, ClaimsPrincipal session)
        {
            var authenticationTime = long.TryParse(session.GetClaim(AuthenticationTimeClaim), out var time) ? time : (long?)null;
            var principal = await CreateAsync(user, session.GetScopes(), session.GetClaims(AuthenticationMethodClaim), authenticationTime);
            // OpenIddict would carry over a session's own lifetime (signup's is an hour); IdentityServer4 gave refreshes the client's
            principal.SetAccessTokenLifetime(OpenIddictEngineExtensions.AccessTokenLifetime);
            return principal;
        }
    }
}
