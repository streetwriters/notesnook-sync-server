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
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Validation
{
    public static class GrantResultExtensions
    {
        public static GrantValidationResult ToValidationResult(this GrantResult.Failed failed)
        {
            return new GrantValidationResult(TokenRequestErrors.InvalidGrant)
            {
                Error = failed.Error,
                ErrorDescription = failed.Description
            };
        }

        public static async Task ApplyAsync(this GrantResult result, ExtensionGrantValidationContext context, ITokenGenerationService tokenGenerationService)
        {
            switch (result)
            {
                case GrantResult.Authenticated authenticated:
                    context.Result = new GrantValidationResult(authenticated.User.Id.ToString(), authenticated.AuthenticationMethod);
                    break;
                case GrantResult.Step step:
                    context.Result.IsError = false;
                    context.Result.Subject = await tokenGenerationService.TransformTokenRequestAsync(context.Request, step.User, context.Request.GrantType, [step.Scope]);
                    if (step.AdditionalData != null)
                        context.Result.CustomResponse = new Dictionary<string, object> { ["additional_data"] = step.AdditionalData };
                    break;
                case GrantResult.Failed failed:
                    context.Result = failed.ToValidationResult();
                    break;
            }
        }
    }
}
