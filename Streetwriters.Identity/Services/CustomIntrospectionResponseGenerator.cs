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

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using IdentityServer4.Endpoints.Results;
using IdentityServer4.Models;
using IdentityServer4.ResponseHandling;
using IdentityServer4.Services;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Services
{
    public class CustomIntrospectionResponseGenerator : IntrospectionResponseGenerator
    {
        private UserManager<User> UserManager { get; }
        public CustomIntrospectionResponseGenerator(IEventService events, ILogger<IntrospectionResponseGenerator> logger, UserManager<User> userManager) : base(events, logger)
        {
            UserManager = userManager;
        }

        public override async Task<Dictionary<string, object>> ProcessAsync(IntrospectionRequestValidationResult validationResult)
        {
            var result = await base.ProcessAsync(validationResult);

            if (result.TryGetValue("sub", out object userId))
            {
                var user = await UserManager.FindByIdAsync(userId.ToString());

                var verifiedClaim = user.Claims.Find((c) => c.ClaimType == "verified");
                if (verifiedClaim != null)
                    await UserManager.RemoveClaimAsync(user, verifiedClaim.ToClaim());
                var hcliClaim = user.Claims.Find((c) => c.ClaimType == "hcli");
                if (hcliClaim != null)
                    await UserManager.RemoveClaimAsync(user, hcliClaim.ToClaim());

                user.Claims.ForEach((claim) =>
                {
                    if (claim.ClaimType == "verified" || claim.ClaimType == "hcli") return;
                    result.TryAdd(claim.ClaimType, claim.ClaimValue);
                });
                result.TryAdd("verified", user.EmailConfirmed.ToString().ToLowerInvariant());
            }
            return result;
        }
    }
}