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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Streetwriters.Common;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Legacy;
using Streetwriters.Identity.Models;
using Streetwriters.Identity.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// The token endpoint, once OpenIddict has validated the request. Answers as IdentityServer4 did:
    /// sessions are issued by OpenIddict, everything else is written here in IdentityServer4's shape.
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class TokenController(GrantService grants, StepTokens stepTokens, SessionPrincipals principals, TokenRecords records, IOpenIddictTokenManager tokens, LegacyGrants legacyGrants, TimeProvider timeProvider, UserManager<User> userManager) : ControllerBase
    {
        /// <summary>
        /// Refreshing with a refresh token IdentityServer4 issued, which OpenIddict can't read. Requests
        /// to refresh with one are rewritten to this before OpenIddict validates them (see OpenIddictEngineExtensions).
        /// </summary>
        public const string LegacyRefreshGrantType = "legacy_refresh_token";
        public const string LegacyRefreshTokenParameter = "legacy_refresh_token";

        // what IdentityServer4 granted when the request named no scopes: all of the client's valid ones
        private static readonly string[] DefaultScopes = [AccountAuthenticationHandler.Scope, OpenIddictEngineExtensions.SyncScope, Scopes.OfflineAccess, Scopes.OpenId];

        [HttpPost("~/connect/token")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ExchangeAsync()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
            var client = Clients.FindClientById(request.ClientId);
            if (client == null) return Error("invalid_client");

            switch (request.GrantType)
            {
                case GrantTypes.Password:
                    return await RespondAsync(request, await grants.PasswordAsync(request.Username ?? "", request.Password ?? "", (string?)request["mfa:code"], (string?)request["mfa:method"]));
                case Config.EMAIL_GRANT_TYPE:
                    return await RespondAsync(request, await grants.EmailAsync((string?)request["email"], request.ClientId!, (string?)request["mfa:optional"] == "true"));
                case Config.MFA_GRANT_TYPE:
                    {
                        var (subject, error) = await ValidateStepTokenAsync(request, Config.MFA_GRANT_TYPE_SCOPE);
                        if (subject == null) return Error(error!);
                        return await RespondAsync(request, await grants.MFAAsync(subject, client, (string?)request["mfa:code"], (string?)request["mfa:method"], (string?)request["mfa:optional"] == "true", HttpContext));
                    }
                case Config.MFA_PASSWORD_GRANT_TYPE:
                    {
                        var (subject, error) = await ValidateStepTokenAsync(request, Config.MFA_PASSWORD_GRANT_TYPE_SCOPE);
                        if (subject == null) return Error(error!);
                        return await RespondAsync(request, await grants.MFAPasswordAsync(subject, client, (string?)request["password"], HttpContext));
                    }
                case GrantTypes.RefreshToken:
                    return await RefreshAsync();
                case LegacyRefreshGrantType:
                    return await LegacyRefreshAsync(request);
                default:
                    return Error("unsupported_grant_type");
            }
        }

        /// <summary>The step token of the previous step, which the client sends as a bearer token.</summary>
        private async Task<(string? Subject, string? Error)> ValidateStepTokenAsync(OpenIddictRequest request, string scope)
        {
            var result = await HttpContext.AuthenticateAsync("Bearer+jwt");
            if (!result.Succeeded || !result.Principal.HasClaim("scope", scope)) return (null, "invalid_grant");
            if (result.Principal.FindFirst("client_id")?.Value != request.ClientId) return (null, "invalid_client");
            var subject = result.Principal.FindFirst("sub")?.Value;
            return string.IsNullOrEmpty(subject) ? (null, "invalid_grant") : (subject, null);
        }

        private async Task<IActionResult> RespondAsync(OpenIddictRequest request, GrantResult result)
        {
            switch (result)
            {
                case GrantResult.Authenticated authenticated:
                    var scopes = request.GetScopes().Any() ? request.GetScopes() : [.. DefaultScopes];
                    var principal = await principals.CreateAsync(authenticated.User, scopes.Order(StringComparer.Ordinal), [authenticated.AuthenticationMethod]);
                    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                case GrantResult.Step step:
                    return Json(200, (writer) =>
                    {
                        writer.WriteString("access_token", stepTokens.Create(step.User.Id.ToString(), request.ClientId!, step.Scope));
                        writer.WriteNumber("expires_in", StepTokens.Lifetime);
                        writer.WriteString("token_type", "Bearer");
                        writer.WriteString("scope", step.Scope);
                        if (step.AdditionalData != null)
                        {
                            writer.WritePropertyName("additional_data");
                            // IdentityServer4's responses left out nulls
                            JsonSerializer.Serialize(writer, step.AdditionalData, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                        }
                    });

                case GrantResult.MFARequired mfaRequired:
                    mfaRequired.Data.Token = stepTokens.Create(mfaRequired.User.Id.ToString(), request.ClientId!, Config.MFA_GRANT_TYPE_SCOPE);
                    // IdentityServer4 wrote its own error first, which clients read past
                    return Json(400, (writer) =>
                    {
                        writer.WriteString("error", "invalid_grant");
                        writer.WriteString("error_description", "invalid_username_or_password");
                        writer.WriteString("error", "mfa_required");
                        writer.WriteString("error_description", "Multifactor authentication required.");
                        writer.WriteString("data", JsonSerializer.Serialize(mfaRequired.Data));
                    });

                case GrantResult.Failed failed when failed == GrantResult.InvalidGrant && request.IsPasswordGrantType():
                    return Error("invalid_grant", "invalid_username_or_password");

                case GrantResult.Failed failed:
                    return Error(failed.Error, failed.Description);

                default:
                    throw new InvalidOperationException($"Unknown grant result {result}.");
            }
        }

        private async Task<IActionResult> RefreshAsync()
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var session = result.Principal;
            var subject = session?.GetClaim(Claims.Subject);
            var user = subject != null ? await userManager.FindByIdAsync(subject) : null;
            if (session == null || user == null) return Error("invalid_grant");

            // Using a refresh token proves the client received it, so the token it replaced, and any other
            // tokens that replaced that one (from concurrent refreshes or lost responses), must stop working.
            var tokenId = session.GetTokenId()!;
            var record = await records.FindAsync(tokenId);
            if (record?.Replaced != null) await RevokeReplacedAsync(record.Replaced, tokenId);

            var principal = await principals.RefreshAsync(user, session);
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { [RecordIssuedTokens.ReplacedTokenProperty] = tokenId });
            return SignIn(principal, properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Refreshes a session IdentityServer4 started, as CustomRefreshTokenService did, into an OpenIddict
        /// session. The legacy token keeps working until the token that replaces it is used.
        /// </summary>
        private async Task<IActionResult> LegacyRefreshAsync(OpenIddictRequest request)
        {
            var grant = await legacyGrants.FindAsync((string?)request[LegacyRefreshTokenParameter] ?? "", LegacyGrants.RefreshTokenType);
            if (grant == null || legacyGrants.IsExpired(grant) || grant.ClientId != request.ClientId) return Error("invalid_grant");
            var user = await userManager.FindByIdAsync(grant.SubjectId);
            if (user == null) return Error("invalid_grant");

            if (grant.ReplacedKey != null) await RevokeReplacedAsync(LegacyGrants.LineagePrefix + grant.ReplacedKey, LegacyGrants.LineagePrefix + grant.Key);
            await legacyGrants.MarkConsumedAsync(grant.Key);

            var session = LegacyGrants.ToPrincipal(grant);
            var principal = await principals.RefreshAsync(user, session);
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { [RecordIssuedTokens.ReplacedTokenProperty] = LegacyGrants.LineagePrefix + grant.Key });
            return SignIn(principal, properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Using a refresh token proves the client received it, so the token it replaced, and any other
        /// tokens that replaced that one (from concurrent refreshes or lost responses), must stop working.
        /// </summary>
        /// <param name="replaced">The replaced token: an OpenIddict token id, or a legacy grant's key with <see cref="LegacyGrants.LineagePrefix"/>.</param>
        /// <param name="used">The token being used, in the same form.</param>
        private async Task RevokeReplacedAsync(string replaced, string used)
        {
            await RevokeAsync(replaced);
            foreach (var sibling in await records.FindReplacementsAsync(replaced))
            {
                if (sibling.TokenId != used) await RevokeAsync(sibling.TokenId);
            }
            if (replaced.StartsWith(LegacyGrants.LineagePrefix))
            {
                foreach (var key in await legacyGrants.FindReplacementsAsync(replaced[LegacyGrants.LineagePrefix.Length..]))
                {
                    if (LegacyGrants.LineagePrefix + key != used) await legacyGrants.RemoveAsync(key);
                }
            }
        }

        private async Task RevokeAsync(string token)
        {
            if (token.StartsWith(LegacyGrants.LineagePrefix)) await legacyGrants.RemoveAsync(token[LegacyGrants.LineagePrefix.Length..]);
            else if (await tokens.FindByIdAsync(token) is object entry) await tokens.TryRevokeAsync(entry);
        }

        private static ContentResult Error(string error, string? description = null) => Json(400, (writer) =>
        {
            writer.WriteString("error", error);
            if (description != null) writer.WriteString("error_description", description);
        });

        /// <summary>Writes a JSON object directly, since IdentityServer4 sometimes repeated properties.</summary>
        private static ContentResult Json(int status, Action<Utf8JsonWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { SkipValidation = true }))
            {
                writer.WriteStartObject();
                write(writer);
                writer.WriteEndObject();
            }
            return new ContentResult { StatusCode = status, Content = Encoding.UTF8.GetString(stream.ToArray()), ContentType = "application/json; charset=UTF-8" };
        }
    }
}
