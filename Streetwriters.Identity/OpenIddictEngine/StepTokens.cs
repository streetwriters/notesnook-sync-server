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
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using Streetwriters.Common;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// The short-lived JWTs that carry users from one sign in step to the next (see GrantService). They hold no
    /// session, so they're signed with the server's key and never stored, as IdentityServer4 issued them.
    /// The Bearer+jwt scheme validates them.
    /// </summary>
    public class StepTokens(IOptionsMonitor<OpenIddictServerOptions> options, TimeProvider timeProvider)
    {
        public const int Lifetime = 20 * 60;

        public string Create(string subject, string clientId, string scope)
        {
            var server = options.CurrentValue;
            var now = timeProvider.GetUtcNow();
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                // as IdentityServer4 wrote it; OpenIddict's own adds a trailing slash
                Issuer = Servers.IdentityServer.ToString(),
                TokenType = "at+jwt",
                SigningCredentials = server.SigningCredentials.First(),
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = now.AddSeconds(Lifetime).UtcDateTime,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = subject,
                    ["client_id"] = clientId,
                    ["scope"] = new[] { scope },
                    ["jti"] = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    ["auth_time"] = now.ToUnixTimeSeconds(),
                    ["idp"] = "local",
                }
            });
        }
    }
}
