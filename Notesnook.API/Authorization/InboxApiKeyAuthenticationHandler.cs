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
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notesnook.API.Models;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Authorization
{
    public static class InboxApiKeyAuthenticationDefaults
    {
        public const string AuthenticationScheme = "InboxApiKey";
    }

    public class InboxApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
    {
    }

    public class InboxApiKeyAuthenticationHandler : AuthenticationHandler<InboxApiKeyAuthenticationSchemeOptions>
    {
        private readonly Repository<InboxApiKey> _inboxApiKeyRepository;

        public InboxApiKeyAuthenticationHandler(
            IOptionsMonitor<InboxApiKeyAuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            Repository<InboxApiKey> inboxApiKeyRepository)
            : base(options, logger, encoder)
        {
            _inboxApiKeyRepository = inboxApiKeyRepository;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return AuthenticateResult.Fail("Missing Authorization header");
            }

            var apiKey = Request.Headers["Authorization"].ToString().Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                return AuthenticateResult.Fail("Missing API key");
            }

            try
            {
                var inboxApiKey = await _inboxApiKeyRepository.FindOneAsync(k => k.Key == apiKey);
                if (inboxApiKey == null)
                {
                    return AuthenticateResult.Fail("Invalid API key");
                }

                if (inboxApiKey.ExpiryDate > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > inboxApiKey.ExpiryDate)
                {
                    return AuthenticateResult.Fail("API key has expired");
                }

                inboxApiKey.LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _inboxApiKeyRepository.UpsertAsync(inboxApiKey, k => k.Key == apiKey);

                var claims = new[]
                {
                    new Claim("sub", inboxApiKey.UserId),
                };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error validating inbox API key");
                return AuthenticateResult.Fail("Error validating API key");
            }
        }
    }
}