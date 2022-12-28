// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using IdentityServer4.Services;
using IdentityServer4.Stores;
using IdentityServer4.Validation;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;

namespace IdentityServer4.ResponseHandling
{
    /// <summary>
    /// The default token response generator
    /// </summary>
    /// <seealso cref="IdentityServer4.ResponseHandling.ITokenResponseGenerator" />
    public class TokenResponseHandler : TokenResponseGenerator, ITokenResponseGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TokenResponseGenerator" /> class.
        /// </summary>
        /// <param name="clock">The clock.</param>
        /// <param name="tokenService">The token service.</param>
        /// <param name="refreshTokenService">The refresh token service.</param>
        /// <param name="scopeParser">The scope parser.</param>
        /// <param name="resources">The resources.</param>
        /// <param name="clients">The clients.</param>
        /// <param name="logger">The logger.</param>
        public TokenResponseHandler(ISystemClock clock, ITokenService tokenService, IRefreshTokenService refreshTokenService, IScopeParser scopeParser, IResourceStore resources, IClientStore clients, ILogger<TokenResponseGenerator> logger)
        : base(clock, tokenService, refreshTokenService, scopeParser, resources, clients, logger)
        {

        }

        protected override async Task<TokenResponse> ProcessRefreshTokenRequestAsync(TokenRequestValidationResult request)
        {
            var response = await base.ProcessRefreshTokenRequestAsync(request);
            // Fixes: https://github.com/IdentityServer/IdentityServer3/issues/3621
            response.IdentityToken = null;
            return response;
        }
    }
}