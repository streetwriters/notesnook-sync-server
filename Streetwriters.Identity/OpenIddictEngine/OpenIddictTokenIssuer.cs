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
using System.Security.Claims;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>Issues tokens outside the token endpoint (e.g. on signup, which comes over WAMP) by running OpenIddict's sign-in directly.</summary>
    public class OpenIddictTokenIssuer(IOpenIddictServerFactory factory, IOpenIddictServerDispatcher dispatcher, SessionPrincipals principals) : ITokenIssuer
    {
        // IdentityServer4 gave these sessions all of the client's allowed scopes, in their order; the apps send them back on refresh
        private static readonly string[] ClientScopes = [OpenIddictEngineExtensions.SyncScope, Scopes.OpenId, AccountAuthenticationHandler.Scope, "mfa", Scopes.OfflineAccess];

        public async Task<IssuedTokens?> IssueUserTokensAsync(User user, string clientId, int lifetime)
        {
            var principal = await principals.CreateAsync(user, ClientScopes, []);
            principal.SetAccessTokenLifetime(TimeSpan.FromSeconds(lifetime));
            var response = await SignInAsync(principal, clientId);
            return new IssuedTokens(response.AccessToken!, response.RefreshToken!, lifetime, string.Join(" ", ClientScopes));
        }

        public async Task<string> IssueAccessTokenAsync(User user, string clientId, int lifetime, string[] authenticationMethods)
        {
            var principal = await principals.CreateAsync(user, [OpenIddictEngineExtensions.SyncScope, AccountAuthenticationHandler.Scope], authenticationMethods);
            principal.SetAccessTokenLifetime(TimeSpan.FromSeconds(lifetime));
            return (await SignInAsync(principal, clientId)).AccessToken!;
        }

        /// <summary>Marks the transactions of these sign-ins, whose response is read here instead of being sent.</summary>
        public const string DirectSignInProperty = "direct_sign_in";

        private async Task<OpenIddictResponse> SignInAsync(ClaimsPrincipal principal, string clientId)
        {
            var transaction = await factory.CreateTransactionAsync();
            transaction.Properties[DirectSignInProperty] = true;
            transaction.EndpointType = OpenIddictServerEndpointType.Token;
            transaction.Request = new OpenIddictRequest { ClientId = clientId, GrantType = GrantTypes.Password };
            var context = new ProcessSignInContext(transaction)
            {
                Principal = principal,
                Response = new OpenIddictResponse()
            };
            await dispatcher.DispatchAsync(context);
            if (context.IsRejected) throw new InvalidOperationException($"Failed to issue tokens: {context.Error} {context.ErrorDescription}");
            return context.Response;
        }
    }
}
