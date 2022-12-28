/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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

using IdentityServer4;
using IdentityServer4.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Streetwriters.Identity
{
    public static class Config
    {
        public const string EMAIL_GRANT_TYPE = "email";
        public const string MFA_GRANT_TYPE = "mfa";
        public const string MFA_PASSWORD_GRANT_TYPE = "mfa_password";

        public const string MFA_GRANT_TYPE_SCOPE = "auth:grant_types:mfa";
        public const string MFA_PASSWORD_GRANT_TYPE_SCOPE = "auth:grant_types:mfa_password";

        public static IEnumerable<IdentityResource> IdentityResources =>
            new List<IdentityResource> {
                new IdentityResources.OpenId(),
            };

        public static IEnumerable<ApiResource> ApiResources =>
            new List<ApiResource>
            {
                new ApiResource("notesnook", "Notesnook API", new string[] { "verified" })
                {
                    ApiSecrets = { new Secret(Environment.GetEnvironmentVariable("NOTESNOOK_API_SECRET")?.Sha256()) },
                    Scopes = { "notesnook.sync" }
                },
                // local API
                new ApiResource(IdentityServerConstants.LocalApi.ScopeName)
            };

        public static IEnumerable<ApiScope> ApiScopes =>
            new List<ApiScope>
            {
                new ApiScope("notesnook.sync", "Notesnook Syncing Access"),
                new ApiScope(IdentityServerConstants.LocalApi.ScopeName),
                new ApiScope(MFA_GRANT_TYPE_SCOPE, "Multi-factor authentication access"),
                new ApiScope(MFA_PASSWORD_GRANT_TYPE_SCOPE, "Multi-factor authentication password step access")
            };

        public static IEnumerable<Client> Clients =>
            new List<Client>
            {
                new Client
                {
                    ClientName = "Notesnook",
                    ClientId = "notesnook",
                    AllowedGrantTypes = { GrantType.ResourceOwnerPassword, MFA_GRANT_TYPE, MFA_PASSWORD_GRANT_TYPE, EMAIL_GRANT_TYPE, },
                    RequirePkce = false,
                    RequireClientSecret = false,
                    RequireConsent = false,
                    AccessTokenType = AccessTokenType.Reference,
                    AllowOfflineAccess = true,
                    UpdateAccessTokenClaimsOnRefresh = true,
                    RefreshTokenUsage = TokenUsage.OneTimeOnly,
                    RefreshTokenExpiration = TokenExpiration.Absolute,
                    AccessTokenLifetime = 3600,
                    
                    // scopes that client has access to
                    AllowedScopes = { "notesnook.sync", "offline_access", "openid", IdentityServerConstants.LocalApi.ScopeName, "mfa" },
                }
            };
    }
}