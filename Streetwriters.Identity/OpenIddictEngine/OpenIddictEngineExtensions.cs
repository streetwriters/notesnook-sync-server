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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Streetwriters.Common;
using Streetwriters.Identity.Interfaces;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Streetwriters.Identity.OpenIddictEngine
{
    public static class OpenIddictEngineExtensions
    {
        public const string SyncScope = "notesnook.sync";

        public static readonly string[] Scopes =
        [
            SyncScope,
            AccountAuthenticationHandler.Scope,
            Config.MFA_GRANT_TYPE_SCOPE,
            Config.MFA_PASSWORD_GRANT_TYPE_SCOPE,
            OpenIddictConstants.Scopes.OfflineAccess,
            OpenIddictConstants.Scopes.OpenId,
        ];

        public static IServiceCollection AddOpenIddictEngine(this IServiceCollection services, string connectionString)
        {
            var url = MongoUrl.Create(connectionString);
            var database = new MongoClient(url).GetDatabase(url.DatabaseName);

            services.AddOpenIddict()
                .AddCore((options) => options.UseMongoDb().UseDatabase(database))
                .AddServer((options) =>
                {
                    options.SetIssuer(Servers.IdentityServer.ToString());
                    options.SetTokenEndpointUris("connect/token");
                    options.AllowPasswordFlow()
                        .AllowRefreshTokenFlow()
                        .AllowCustomFlow(Config.EMAIL_GRANT_TYPE)
                        .AllowCustomFlow(Config.MFA_GRANT_TYPE)
                        .AllowCustomFlow(Config.MFA_PASSWORD_GRANT_TYPE);
                    options.RegisterScopes(Scopes);

                    options.SetAccessTokenLifetime(TimeSpan.FromHours(6));
                    options.SetRefreshTokenLifetime(TimeSpan.FromDays(45));
                    // refresh tokens stay usable until their replacement is used, as with IdentityServer4 (see TokenController)
                    options.SetRefreshTokenReuseLeeway(TimeSpan.FromDays(45));
                    options.UseReferenceAccessTokens().UseReferenceRefreshTokens();

                    options.AddSigningKey(TokenKeys.LoadSigningKey(database));
                    options.AddEncryptionKey(TokenKeys.LoadEncryptionKey(database));

                    // TLS ends at the proxy
                    options.UseAspNetCore().EnableTokenEndpointPassthrough().DisableTransportSecurityRequirement();

                    options.AddEventHandler(RecordIssuedTokens.Descriptor);

                    // IdentityServer4 didn't issue identity tokens from the token endpoint
                    options.AddEventHandler<ProcessSignInContext>((builder) => builder
                        .UseInlineHandler((context) =>
                        {
                            context.GenerateIdentityToken = false;
                            return default;
                        })
                        .SetOrder(OpenIddictServerHandlers.EvaluateGeneratedTokens.Descriptor.Order + 1));

                    // introspection and revocation are answered by IntrospectionController and RevocationController
                    options.AddEventHandler<HandleConfigurationRequestContext>((builder) => builder
                        .UseInlineHandler((context) =>
                        {
                            context.IntrospectionEndpoint = new Uri(context.Options.Issuer!, "connect/introspect");
                            context.RevocationEndpoint = new Uri(context.Options.Issuer!, "connect/revocation");
                            return default;
                        })
                        .SetOrder(OpenIddictServerHandlers.Discovery.AttachEndpoints.Descriptor.Order + 1));

                    // IdentityServer4 gave the lifetime it issued (OpenIddict counts down from the moment it answers),
                    // and always the granted scopes
                    options.AddEventHandler<ProcessSignInContext>((builder) => builder
                        .UseInlineHandler((context) =>
                        {
                            if (context.AccessTokenPrincipal is { } principal && context.Response.AccessToken != null)
                            {
                                if (principal.GetCreationDate() is { } created && principal.GetExpirationDate() is { } expires)
                                    context.Response.ExpiresIn = (long)(expires - created).TotalSeconds;
                                context.Response.Scope = string.Join(" ", principal.GetScopes());
                            }
                            return default;
                        })
                        .SetOrder(OpenIddictServerHandlers.AttachSignInParameters.Descriptor.Order + 1));

                    options.AddEventHandler<ApplyTokenResponseContext>((builder) => builder
                        .UseInlineHandler(ShapeTokenErrors)
                        .SetOrder(int.MinValue));

                    // OpenIddictTokenIssuer reads the response itself
                    options.AddEventHandler<ApplyTokenResponseContext>((builder) => builder
                        .UseInlineHandler((context) =>
                        {
                            if (context.Transaction.Properties.ContainsKey(OpenIddictTokenIssuer.DirectSignInProperty)) context.HandleRequest();
                            return default;
                        })
                        .SetOrder(int.MaxValue));
                })
                .AddValidation((options) =>
                {
                    options.UseLocalServer();
                    options.EnableTokenEntryValidation();
                });

            services.AddHostedService<StoreIndexes>();
            services.AddHostedService<ClientRegistration>();
            services.AddHostedService<TokenPruning>();
            services.AddSingleton((_) => new TokenRecords(database));
            services.AddTransient<StepTokens>();
            services.AddTransient<SessionPrincipals>();
            services.AddTransient<AccessTokens>();
            services.AddTransient<ITokenIssuer, OpenIddictTokenIssuer>();
            services.AddTransient<ISessionStore, OpenIddictSessionStore>();

            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AccountAuthenticationHandler>(AccountAuthenticationHandler.Scheme, null);
            services.AddAuthorizationBuilder().AddPolicy(Config.ACCOUNT_API_POLICY, (policy) => policy
                .AddAuthenticationSchemes(AccountAuthenticationHandler.Scheme)
                .RequireAuthenticatedUser());
            return services;
        }

        /// <summary>
        /// IdentityServer4 sent only the error code for protocol errors, and named some differently.
        /// Errors of sign in grants are written by TokenController and don't come through here.
        /// </summary>
        private static ValueTask ShapeTokenErrors(ApplyTokenResponseContext context)
        {
            if (string.IsNullOrEmpty(context.Response.Error)) return default;

            if (string.IsNullOrEmpty(context.Request?.GrantType))
                context.Response.Error = Errors.UnsupportedGrantType;
            else if (context.Response.Error == Errors.UnsupportedGrantType
                && context.Request.GrantType is GrantTypes.ClientCredentials or GrantTypes.AuthorizationCode or GrantTypes.DeviceCode or GrantTypes.Implicit)
                context.Response.Error = Errors.UnauthorizedClient;

            context.Response.ErrorDescription = null;
            context.Response.ErrorUri = null;
            return default;
        }

        /// <summary>Cache headers on the token endpoints' responses, as IdentityServer4 sent them.</summary>
        public static IApplicationBuilder UseOpenIddictEngine(this IApplicationBuilder app)
        {
            return app.Use((context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/connect"))
                {
                    context.Response.OnStarting(() =>
                    {
                        // IdentityServer4 answered client errors at the token endpoint with 400, not 401
                        if (context.Request.Path.StartsWithSegments("/connect/token") && context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            context.Response.Headers.WWWAuthenticate = default;
                        }
                        // only responses with a body had these
                        if (context.Response.ContentType?.StartsWith("application/json") != true) return Task.CompletedTask;
                        context.Response.ContentType = "application/json; charset=UTF-8";
                        context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                        context.Response.Headers.Pragma = "no-cache";
                        return Task.CompletedTask;
                    });
                }
                return next(context);
            });
        }
    }
}
