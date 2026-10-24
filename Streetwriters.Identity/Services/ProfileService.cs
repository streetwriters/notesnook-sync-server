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
using IdentityModel;
using IdentityServer4;
using IdentityServer4.Models;
using IdentityServer4.Services;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;
using Streetwriters.Data.Repositories;

namespace Streetwriters.Identity.Services
{
    public class ProfileService : IProfileService
    {
        protected UserManager<User> UserManager { get; set; }

        public ProfileService(UserManager<User> userManager)
        {
            UserManager = userManager;
        }

        public async Task GetProfileDataAsync(ProfileDataRequestContext context)
        {
            // step tokens are readable by anyone signing in, and must not tell registered and unregistered users apart
            if (IsStep(context.RequestedResources?.RawScopeValues)) return;

            User? user = await UserManager.GetUserAsync(context.Subject);
            if (user == null) return;

            IList<string> roles = await UserManager.GetRolesAsync(user);
            IList<Claim> claims = user.Claims.Select((c) => c.ToClaim()).ToList();

            context.IssuedClaims.AddRange(roles.Select((r) => new Claim(JwtClaimTypes.Role, r)));
            context.IssuedClaims.AddRange(claims);
        }

        public async Task IsActiveAsync(IsActiveContext context)
        {
            // deleted users must not be able to refresh or introspect their tokens
            context.IsActive = await UserManager.GetUserAsync(context.Subject) != null
                // unregistered users go through the email and mfa steps like registered ones (see GrantService)
                || context.Caller == IdentityServerConstants.ProfileIsActiveCallers.ExtensionGrantValidation
                || IsStep(context.Subject.FindAll(JwtClaimTypes.Scope).Select((c) => c.Value));
        }

        private static bool IsStep(IEnumerable<string>? scopes)
        {
            return scopes != null && scopes.Any() && scopes.All((scope) => scope == Config.MFA_GRANT_TYPE_SCOPE || scope == Config.MFA_PASSWORD_GRANT_TYPE_SCOPE);
        }
    }
}