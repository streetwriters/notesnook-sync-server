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
using MongoDB.Driver;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
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
        private readonly UserGenerations _generations;

        public InboxApiKeyAuthenticationHandler(
            IOptionsMonitor<InboxApiKeyAuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            Repository<InboxApiKey> inboxApiKeyRepository,
            UserGenerations generations)
            : base(options, logger, encoder)
        {
            _inboxApiKeyRepository = inboxApiKeyRepository;
            _generations = generations;
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

            var inboxApiKey = await _inboxApiKeyRepository.FindOneAsync(k => k.Key == apiKey);
            if (inboxApiKey == null)
            {
                return AuthenticateResult.Fail("Invalid API key");
            }

            if (inboxApiKey.ExpiryDate > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > inboxApiKey.ExpiryDate)
            {
                return AuthenticateResult.Fail("API key has expired");
            }
            // a key from before a reset is invalid even if the reset didn't get to delete it
            if (inboxApiKey.Generation != await _generations.ReadAsync(inboxApiKey.UserId))
            {
                return AuthenticateResult.Fail("Invalid API key");
            }
            _generations.Pin(inboxApiKey.UserId, inboxApiKey.Generation);

            // not an upsert, which would recreate a key deleted while this request was in flight
            await _inboxApiKeyRepository.Collection.UpdateOneAsync(
                Builders<InboxApiKey>.Filter.Eq(k => k.Key, apiKey),
                Builders<InboxApiKey>.Update.Set(k => k.LastUsedAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

            var claims = new[]
            {
                new Claim("sub", inboxApiKey.UserId),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}