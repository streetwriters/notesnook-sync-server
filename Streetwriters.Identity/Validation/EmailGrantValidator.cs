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

using System.Threading.Tasks;
using IdentityServer4.Validation;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Validation
{
    public class EmailGrantValidator(GrantService grants, ITokenGenerationService tokenGenerationService) : IExtensionGrantValidator
    {
        public string GrantType => Config.EMAIL_GRANT_TYPE;

        public async Task ValidateAsync(ExtensionGrantValidationContext context)
        {
            var result = await grants.EmailAsync(context.Request.Raw["email"], context.Request.ClientId, context.Request.Raw["mfa:optional"] == "true");
            await result.ApplyAsync(context, tokenGenerationService);
        }
    }
}
