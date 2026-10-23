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
using System.Text.Json;
using System.Threading.Tasks;
using IdentityServer4.Validation;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Validation
{
    public class CustomResourceOwnerValidator(GrantService grants, ITokenGenerationService tokenGenerationService) : IResourceOwnerPasswordValidator
    {
        public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
        {
            var result = await grants.PasswordAsync(context.UserName, context.Password, context.Request.Raw["mfa:code"], context.Request.Raw["mfa:method"]);
            switch (result)
            {
                case GrantResult.Authenticated authenticated:
                    context.Result = new GrantValidationResult(authenticated.User.Id.ToString(), authenticated.AuthenticationMethod);
                    break;
                case GrantResult.MFARequired mfaRequired:
                    mfaRequired.Data.Token = await tokenGenerationService.CreateAccessTokenFromValidatedRequestAsync(context.Request, mfaRequired.User, [Config.MFA_GRANT_TYPE_SCOPE]);
                    // IdentityServer4 writes this alongside the context's default error, which clients override with these
                    context.Result.CustomResponse = new Dictionary<string, object>
                    {
                        ["error"] = "mfa_required",
                        ["error_description"] = "Multifactor authentication required.",
                        ["data"] = JsonSerializer.Serialize(mfaRequired.Data)
                    };
                    context.Result.IsError = true;
                    break;
                case GrantResult.Failed failed:
                    context.Result = failed.ToValidationResult();
                    break;
            }
        }
    }
}
