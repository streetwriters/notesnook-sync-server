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
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Notesnook.API.Authorization;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Messages;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("inbox")]
    public class InboxController(
            Repository<InboxApiKey> inboxApiKeysRepository,
            Repository<UserSettings> userSettingsRepository,
            Repository<InboxSyncItem> inboxItemsRepository,
            ILogger<InboxController> logger) : ControllerBase
    {

        [HttpGet("api-keys")]
        [Authorize(Policy = "Notesnook")]
        public async Task<IActionResult> GetApiKeysAsync()
        {
            var userId = User.GetUserId();
            try
            {
                var apiKeys = await inboxApiKeysRepository.FindAsync(t => t.UserId == userId);
                return Ok(apiKeys);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't get inbox api keys for user {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("api-keys")]
        [Authorize(Policy = "Notesnook")]
        public async Task<IActionResult> CreateApiKeyAsync([FromBody] InboxApiKey request)
        {
            var userId = User.GetUserId();
            try
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest(new { error = "Api key name is required." });
                }
                if (request.ExpiryDate <= -1)
                {
                    return BadRequest(new { error = "Valid expiry date is required." });
                }

                var count = await inboxApiKeysRepository.CountAsync(t => t.UserId == userId);
                if (count >= 10)
                {
                    return BadRequest(new { error = "Maximum of 10 inbox api keys allowed." });
                }

                var inboxApiKey = new InboxApiKey
                {
                    UserId = userId,
                    Name = request.Name,
                    DateCreated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ExpiryDate = request.ExpiryDate,
                    LastUsedAt = 0
                };
                await inboxApiKeysRepository.InsertAsync(inboxApiKey);
                return Ok(inboxApiKey);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't create inbox api key for {UserId}.", userId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("api-keys/{apiKey}")]
        [Authorize(Policy = "Notesnook")]
        public async Task<IActionResult> DeleteApiKeyAsync(string apiKey)
        {
            var userId = User.GetUserId();
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return BadRequest(new { error = "Api key is required." });
                }

                await inboxApiKeysRepository.DeleteAsync(t => t.UserId == userId && t.Key == apiKey);
                return Ok(new { message = "Api key deleted successfully." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't delete inbox api key for user {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("public-encryption-key")]
        [Authorize(Policy = InboxApiKeyAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetPublicKeyAsync()
        {
            var userId = User.GetUserId();
            try
            {
                var userSetting = await userSettingsRepository.FindOneAsync(u => u.UserId == userId);
                if (string.IsNullOrWhiteSpace(userSetting?.InboxKeys?.Public))
                {
                    return BadRequest(new { error = "Inbox public key is not configured." });
                }
                return Ok(new { key = userSetting.InboxKeys.Public });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't get user's inbox's public key for user {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("items")]
        [Authorize(Policy = InboxApiKeyAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> CreateInboxItemAsync([FromBody] InboxSyncItem request)
        {
            var userId = User.GetUserId();
            try
            {
                if (string.IsNullOrWhiteSpace(request.Item))
                {
                    return BadRequest(new { error = "Inbox item is required." });
                }
                if (request.Version <= 0)
                {
                    return BadRequest(new { error = "Valid inbox item version is required." });
                }

                request.UserId = userId;
                request.ItemId = ObjectId.GenerateNewId().ToString();
                await inboxItemsRepository.InsertAsync(request);
                new SyncDeviceService(new SyncDevice(userId, string.Empty))
                    .AddIdsToAllDevices([$"{request.ItemId}:inboxItems"]);
                await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
                {
                    OriginTokenId = null,
                    UserId = userId,
                    Message = new Message
                    {
                        Type = "triggerSync",
                        Data = JsonSerializer.Serialize(new { reason = "Inbox items updated." })
                    }
                });
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't create inbox item for user {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
