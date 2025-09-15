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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notesnook.API.Extensions;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("inbox")]
    public class InboxController : ControllerBase
    {
        private readonly Repository<InboxApiKey> InboxApiKey;

        public InboxController(Repository<InboxApiKey> inboxApiKeysRepository)
        {
            InboxApiKey = inboxApiKeysRepository;
        }

        [HttpGet("api-keys")]
        public async Task<IActionResult> GetApiKeysAsync()
        {
            var userId = User.FindFirstValue("sub");
            try
            {
                var apiKeys = await InboxApiKey.FindAsync(t => t.UserId == userId);
                return Ok(apiKeys);
            }
            catch (Exception ex)
            {
                await Slogger<InboxController>.Error(nameof(GetApiKeysAsync), "Couldn't get inbox api keys.", userId, ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("api-keys")]
        public async Task<IActionResult> CreateApiKeyAsync([FromBody] InboxApiKey request)
        {
            var userId = User.FindFirstValue("sub");
            try
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest(new { error = "Api key name is required." });
                }
                if (request.DateCreated <= 0)
                {
                    return BadRequest(new { error = "Valid creation date is required." });
                }
                if (request.ExpiryDate <= -1)
                {
                    return BadRequest(new { error = "Valid expiry date is required." });
                }

                var count = await InboxApiKey.CountAsync(t => t.UserId == userId);
                if (count >= 10)
                {
                    return BadRequest(new { error = "Maximum of 10 inbox api keys allowed." });
                }

                var inboxApiKey = new InboxApiKey
                {
                    UserId = userId,
                    Name = request.Name,
                    DateCreated = request.DateCreated,
                    ExpiryDate = request.ExpiryDate,
                    LastUsedAt = 0
                };
                inboxApiKey.SetKey();
                await InboxApiKey.InsertAsync(inboxApiKey);
                return Ok(inboxApiKey);
            }
            catch (Exception ex)
            {
                await Slogger<InboxController>.Error(nameof(CreateApiKeyAsync), "Couldn't create inbox api key.", userId, ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("api-keys/{apiKey}")]
        public async Task<IActionResult> DeleteApiKeyAsync(string apiKey)
        {
            var userId = User.FindFirstValue("sub");
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return BadRequest(new { error = "Api key is required." });
                }

                await InboxApiKey.DeleteAsync(t => t.UserId == userId && t.Key == apiKey);
                return Ok(new { message = "Api key deleted successfully." });
            }
            catch (Exception ex)
            {
                await Slogger<InboxController>.Error(nameof(DeleteApiKeyAsync), "Couldn't delete inbox api key.", userId, ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
