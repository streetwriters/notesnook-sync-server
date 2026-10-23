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
using System.Threading.Tasks;
using IdentityServer4.Models;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Http;
using Streetwriters.Common;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Validation
{
    public class MFAPasswordGrantValidator(GrantService grants, IHttpContextAccessor httpContextAccessor, ITokenValidator tokenValidator, ITokenGenerationService tokenGenerationService) : IExtensionGrantValidator
    {
        public string GrantType => Config.MFA_PASSWORD_GRANT_TYPE;

        public async Task ValidateAsync(ExtensionGrantValidationContext context)
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant);

            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            var tokenResult = BearerTokenValidator.ValidateAuthorizationHeader(httpContext);
            if (!tokenResult.TokenFound) return;

            var tokenValidationResult = await tokenValidator.ValidateAccessTokenAsync(tokenResult.Token, Config.MFA_PASSWORD_GRANT_TYPE_SCOPE);
            if (tokenValidationResult.IsError) return;

            var client = Clients.FindClientById(context.Request.ClientId);
            if (client == null || context.Request.ClientId != tokenValidationResult.Claims.GetClaimValue("client_id"))
            {
                context.Result = new GrantValidationResult(TokenRequestErrors.InvalidClient);
                return;
            }

            var userId = tokenValidationResult.Claims.GetClaimValue("sub");
            if (string.IsNullOrEmpty(userId)) return;

            var result = await grants.MFAPasswordAsync(userId, client, context.Request.Raw["password"], httpContext);
            await result.ApplyAsync(context, tokenGenerationService);
        }
    }
}
