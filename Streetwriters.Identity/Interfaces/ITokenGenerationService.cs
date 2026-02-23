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

using System.Security.Claims;
using System.Threading.Tasks;
using IdentityServer4.ResponseHandling;
using IdentityServer4.Validation;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Interfaces
{
    public interface ITokenGenerationService
    {
        Task<string> CreateAccessTokenAsync(User user, string clientId, int lifetime = 1800);
        Task<string> CreateAccessTokenFromValidatedRequestAsync(ValidatedTokenRequest validatedRequest, User user, string[] scopes, int lifetime = 1200);
        Task<ClaimsPrincipal> TransformTokenRequestAsync(ValidatedTokenRequest request, User user, string grantType, string[] scopes, int lifetime = 1200);
        Task<TokenResponse?> CreateUserTokensAsync(User user, string clientId, int lifetime = 1800);
    }
}