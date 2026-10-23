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
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using Streetwriters.Common;
using Streetwriters.Common.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// Introspection for the API servers, as IdentityServer4 answered it to the "notesnook" API resource:
    /// they authenticate with NOTESNOOK_API_SECRET and see only tokens meant for them.
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class IntrospectionController(AccessTokens accessTokens, UserManager<User> userManager) : ControllerBase
    {
        private const string ApiResource = "notesnook";

        // claims the response states itself, or that are OpenIddict's own
        private static readonly HashSet<string> OwnClaims = [Claims.Scope, Claims.Audience, Claims.Issuer, Claims.ExpiresAt, Claims.IssuedAt, Claims.NotBefore];

        [HttpPost("~/connect/introspect")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> IntrospectAsync()
        {
            var form = await Request.ReadFormAsync();
            if (!IsApiResource(form))
            {
                // without the problem details ApiController would add
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return new EmptyResult();
            }

            var token = form["token"].ToString();
            var principal = string.IsNullOrEmpty(token) ? null : await accessTokens.ValidateAsync(token);
            if (principal == null || !principal.HasScope(OpenIddictEngineExtensions.SyncScope))
                return Json(new Dictionary<string, object> { ["active"] = false });

            var response = new Dictionary<string, object>
            {
                ["iss"] = Servers.IdentityServer.ToString(),
                ["nbf"] = principal.GetCreationDate()!.Value.ToUnixTimeSeconds(),
                ["iat"] = principal.GetCreationDate()!.Value.ToUnixTimeSeconds(),
                ["exp"] = principal.GetExpirationDate()!.Value.ToUnixTimeSeconds(),
                ["aud"] = ApiResource,
                ["scope"] = OpenIddictEngineExtensions.SyncScope,
                ["active"] = true,
            };
            foreach (var group in principal.Claims.Where((c) => !c.Type.StartsWith("oi_") && !OwnClaims.Contains(c.Type)).GroupBy((c) => c.Type))
            {
                response[group.Key] = group.Count() == 1 ? Value(group.Single()) : group.Select(Value).ToArray();
            }

            // as CustomIntrospectionResponseGenerator: the user's current claims, with verified and hcli
            // reported once and then removed, and verified otherwise from the email's confirmation
            var user = await userManager.FindByIdAsync(principal.GetClaim(Claims.Subject)!);
            if (user != null)
            {
                foreach (var type in new[] { "verified", "hcli" })
                {
                    var claim = user.Claims.Find((c) => c.ClaimType == type);
                    if (claim != null) await userManager.RemoveClaimAsync(user, claim.ToClaim());
                }
                foreach (var claim in user.Claims)
                {
                    if (claim.ClaimType == null || claim.ClaimType == "verified" || claim.ClaimType == "hcli") continue;
                    response.TryAdd(claim.ClaimType, claim.ClaimValue!);
                }
                response.TryAdd("verified", user.EmailConfirmed.ToString().ToLowerInvariant());
            }
            return Json(response);
        }

        private static object Value(Claim claim) =>
            claim.Type == SessionPrincipals.AuthenticationTimeClaim && long.TryParse(claim.Value, out var time) ? time : claim.Value;

        private bool IsApiResource(IFormCollection form)
        {
            string? id = form["client_id"], secret = form["client_secret"];
            var header = Request.Headers.Authorization.FirstOrDefault();
            if (header != null && header.StartsWith("Basic "))
            {
                try
                {
                    var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim())).Split(':', 2);
                    if (credentials.Length == 2) (id, secret) = (Uri.UnescapeDataString(credentials[0]), Uri.UnescapeDataString(credentials[1]));
                }
                catch (FormatException) { }
            }
            return id == ApiResource && secret != null
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(Constants.NOTESNOOK_API_SECRET));
        }

        private ContentResult Json(Dictionary<string, object> response) =>
            new() { StatusCode = 200, Content = JsonSerializer.Serialize(response), ContentType = "application/json; charset=UTF-8" };
    }
}
