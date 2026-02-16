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

using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using IdentityModel;
using IdentityServer4;
using IdentityServer4.Configuration;
using IdentityServer4.Models;
using IdentityServer4.ResponseHandling;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;

namespace Streetwriters.Identity.Helpers
{
    public class TokenGenerationService : ITokenGenerationService
    {
        private IPersistedGrantStore PersistedGrantStore { get; set; }
        private ITokenService TokenService { get; set; }
        private IUserClaimsPrincipalFactory<User> PrincipalFactory { get; set; }
        private IdentityServerOptions ISOptions { get; set; }
        private IdentityServerTools Tools { get; set; }
        private IResourceStore ResourceStore { get; set; }
        private readonly IRefreshTokenService refreshTokenService;
        public TokenGenerationService(ITokenService tokenService,
        IUserClaimsPrincipalFactory<User> principalFactory,
        IdentityServerOptions identityServerOptions,
        IPersistedGrantStore persistedGrantStore,
        IdentityServerTools tools,
        IResourceStore resourceStore,
        IRefreshTokenService _refreshTokenService)
        {
            TokenService = tokenService;
            PrincipalFactory = principalFactory;
            ISOptions = identityServerOptions;
            PersistedGrantStore = persistedGrantStore;
            Tools = tools;
            ResourceStore = resourceStore;
            refreshTokenService = _refreshTokenService;
        }

        public async Task<string> CreateAccessTokenAsync(User user, string clientId, int lifetime = 1800)
        {
            var client = Config.Clients.FirstOrDefault((c) => c.ClientId == clientId);
            if (client == null)
            {
                throw new System.ArgumentException($"Client with ID '{clientId}' not found", nameof(clientId));
            }

            var IdentityPricipal = await PrincipalFactory.CreateAsync(user);
            var IdentityUser = new IdentityServerUser(user.Id.ToString())
            {
                AdditionalClaims = [.. IdentityPricipal.Claims],
                DisplayName = user.UserName,
                AuthenticationTime = System.DateTime.UtcNow,
                IdentityProvider = IdentityServerConstants.LocalIdentityProvider
            };
            var Request = new TokenCreationRequest
            {
                Subject = IdentityUser.CreatePrincipal(),
                IncludeAllIdentityClaims = true,
                ValidatedRequest = new ValidatedRequest()
            };
            Request.ValidatedRequest.Subject = Request.Subject;
            Request.ValidatedRequest.SetClient(client);
            Request.ValidatedRequest.AccessTokenType = AccessTokenType.Reference;
            Request.ValidatedRequest.AccessTokenLifetime = lifetime;
            var requestedScopes = client.AllowedScopes.Select(s => new ParsedScopeValue(s));
            Request.ValidatedResources = await ResourceStore.CreateResourceValidationResult(new ParsedScopesResult
            {
                ParsedScopes = [.. requestedScopes]
            });
            Request.ValidatedRequest.Options = ISOptions;
            Request.ValidatedRequest.ClientClaims = IdentityUser.AdditionalClaims;
            var accessToken = await TokenService.CreateAccessTokenAsync(Request);
            return await TokenService.CreateSecurityTokenAsync(accessToken);
        }

        public async Task<TokenResponse?> CreateUserTokensAsync(User user, string clientId, int lifetime = 1800)
        {
            var client = Config.Clients.FirstOrDefault((c) => c.ClientId == clientId);
            var principal = await PrincipalFactory.CreateAsync(user);
            if (client == null || principal == null) return null;
            var IdentityUser = new IdentityServerUser(user.Id.ToString())
            {
                AdditionalClaims = [.. principal.Claims],
                DisplayName = user.UserName,
                AuthenticationTime = System.DateTime.UtcNow,
                IdentityProvider = IdentityServerConstants.LocalIdentityProvider
            };
            var Request = new TokenCreationRequest
            {
                Subject = IdentityUser.CreatePrincipal(),
                IncludeAllIdentityClaims = true,
                ValidatedRequest = new ValidatedRequest()
            };
            Request.ValidatedRequest.Subject = Request.Subject;
            Request.ValidatedRequest.SetClient(client);
            Request.ValidatedRequest.AccessTokenType = AccessTokenType.Reference;
            Request.ValidatedRequest.AccessTokenLifetime = lifetime;
            var requestedScopes = client.AllowedScopes.Select(s => new ParsedScopeValue(s));
            Request.ValidatedResources = await ResourceStore.CreateResourceValidationResult(new ParsedScopesResult
            {
                ParsedScopes = [.. requestedScopes]
            });
            Request.ValidatedRequest.Options = ISOptions;
            Request.ValidatedRequest.ClientClaims = IdentityUser.AdditionalClaims;
            var accessToken = await TokenService.CreateAccessTokenAsync(Request);
            var refreshToken = await refreshTokenService.CreateRefreshTokenAsync(principal, accessToken, client);

            return new TokenResponse
            {
                AccessToken = await TokenService.CreateSecurityTokenAsync(accessToken),
                AccessTokenLifetime = lifetime,
                RefreshToken = refreshToken,
                Scope = string.Join(" ", accessToken.Scopes)
            };
        }

        public async Task<ClaimsPrincipal> TransformTokenRequestAsync(ValidatedTokenRequest request, User user, string grantType, string[] scopes, int lifetime = 20 * 60)
        {
            var principal = await PrincipalFactory.CreateAsync(user);
            var identityUser = new IdentityServerUser(user.Id.ToString())
            {
                DisplayName = user.UserName,
                AuthenticationTime = System.DateTime.UtcNow,
                IdentityProvider = IdentityServerConstants.LocalIdentityProvider,
                AdditionalClaims = principal.Claims.ToArray()
            };

            request.AccessTokenType = AccessTokenType.Jwt;
            request.AccessTokenLifetime = lifetime;
            request.GrantType = grantType;
            request.ValidatedResources = await ResourceStore.CreateResourceValidationResult(new ParsedScopesResult()
            {
                ParsedScopes = scopes.Select((scope) => new ParsedScopeValue(scope)).ToArray()
            });
            return identityUser.CreatePrincipal();
        }

        public async Task<string> CreateAccessTokenFromValidatedRequestAsync(ValidatedTokenRequest validatedRequest, User user, string[] scopes, int lifetime = 20 * 60)
        {
            var request = new TokenCreationRequest
            {
                Subject = await this.TransformTokenRequestAsync(validatedRequest, user, validatedRequest.GrantType, scopes, lifetime),
                IncludeAllIdentityClaims = true,
                ValidatedRequest = validatedRequest,
                ValidatedResources = validatedRequest.ValidatedResources
            };
            var accessToken = await TokenService.CreateAccessTokenAsync(request);
            return await TokenService.CreateSecurityTokenAsync(accessToken);
        }
    }
}