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
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>Keeps a <see cref="TokenRecord"/> of the tokens of every session OpenIddict starts or refreshes.</summary>
    public class RecordIssuedTokens(TokenRecords records) : IOpenIddictServerHandler<ProcessSignInContext>
    {
        /// <summary>The sign-in property holding the id of the refresh token being replaced.</summary>
        public const string ReplacedTokenProperty = "replaced_token";

        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
            .UseScopedHandler<RecordIssuedTokens>()
            .SetOrder(OpenIddictServerHandlers.AttachSignInParameters.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

        public async ValueTask HandleAsync(ProcessSignInContext context)
        {
            var subject = context.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
            if (subject == null) return;

            if (context.AccessToken != null && context.AccessTokenPrincipal?.GetTokenId() is string accessTokenId)
            {
                await records.AddAsync(new TokenRecord
                {
                    TokenId = accessTokenId,
                    Type = TokenRecords.AccessToken,
                    Subject = subject,
                    AuthorizationId = context.AccessTokenPrincipal.GetAuthorizationId(),
                    // as IdentityServer4 formatted them; the API servers and clients only compare them
                    Jti = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    CacheKey = (context.AccessToken + ":reference_token").Sha256(),
                    ExpiresAt = ExpirationOf(context.AccessTokenPrincipal),
                });
            }

            if (context.RefreshTokenPrincipal?.GetTokenId() is string refreshTokenId)
            {
                await records.AddAsync(new TokenRecord
                {
                    TokenId = refreshTokenId,
                    Type = TokenRecords.RefreshToken,
                    Subject = subject,
                    AuthorizationId = context.RefreshTokenPrincipal.GetAuthorizationId(),
                    Replaced = context.Properties.TryGetValue(ReplacedTokenProperty, out var replaced) ? replaced as string : null,
                    ExpiresAt = ExpirationOf(context.RefreshTokenPrincipal),
                });
            }
        }

        private static DateTime ExpirationOf(System.Security.Claims.ClaimsPrincipal principal) =>
            (principal.GetExpirationDate() ?? DateTimeOffset.UtcNow.AddDays(46)).UtcDateTime;
    }
}
