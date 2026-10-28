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
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// The account and MFA APIs' authentication, as IdentityServer4's local API authentication: access tokens
    /// with the IdentityServerApi scope.
    /// </summary>
    public class AccountAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AccessTokens accessTokens)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "account";
        public const string Scope = "IdentityServerApi";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.FirstOrDefault();
            // in any case, as IdentityServer4 read it
            if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

            var validated = await accessTokens.ValidateAsync(header["Bearer ".Length..].Trim());
            if (validated is not (var principal, _) || !principal.HasScope(Scope)) return AuthenticateResult.Fail("Invalid access token.");
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
        }
    }
}
