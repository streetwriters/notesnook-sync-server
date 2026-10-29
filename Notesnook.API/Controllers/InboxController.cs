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
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Notesnook.API.Authorization;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
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
            UserGenerations generations,
            SyncDeviceService syncDeviceService) : ControllerBase
    {

        [HttpGet("api-keys")]
        [Authorize(Policy = "Notesnook")]
        public async Task<IActionResult> GetApiKeysAsync()
        {
            var userId = User.GetUserId();
            var apiKeys = await inboxApiKeysRepository.FindAsync(t => t.UserId == userId);
            return Ok(apiKeys);
        }

        [HttpPost("api-keys")]
        [Authorize(Policy = "Notesnook")]
        public async Task<IActionResult> CreateApiKeyAsync([FromBody] CreateInboxApiKeyRequest request)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Api key name is required." });
            }
            if (request.ExpiryDate == null)
            {
                return BadRequest(new { error = "Expiry date is required." });
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
                LastUsedAt = 0,
                Generation = await generations.GetAsync(userId)
            };
            await inboxApiKeysRepository.InsertAsync(inboxApiKey);
            return Ok(inboxApiKey);
        }

        [HttpDelete("api-keys/{apiKey}")]
        [Authorize(Policy = "Notesnook")]
        public async Task<IActionResult> DeleteApiKeyAsync(string apiKey)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return BadRequest(new { error = "Api key is required." });
            }

            await inboxApiKeysRepository.DeleteAsync(t => t.UserId == userId && t.Key == apiKey);
            return Ok(new { message = "Api key deleted successfully." });
        }

        [HttpGet("public-encryption-key")]
        [Authorize(Policy = InboxApiKeyAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetPublicKeyAsync()
        {
            var userId = User.GetUserId();
            var userSetting = await userSettingsRepository.FindOneAsync(u => u.UserId == userId);
            if (string.IsNullOrWhiteSpace(userSetting?.InboxKeys?.Public))
            {
                return NotFound(new { error = "Inbox public key is not configured." });
            }
            return Ok(new { key = userSetting.InboxKeys.Public });
        }

        [HttpPost("items")]
        [Authorize(Policy = InboxApiKeyAuthenticationDefaults.AuthenticationScheme)]
        public async Task<IActionResult> CreateInboxItemAsync([FromBody] InboxSyncItem request)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrWhiteSpace(request.Cipher))
            {
                return BadRequest(new { error = "Inbox item is required." });
            }
            if (string.IsNullOrWhiteSpace(request.Algorithm))
            {
                return BadRequest(new { error = "Inbox item algorithm is required." });
            }
            if (request.Version <= 0)
            {
                return BadRequest(new { error = "Valid inbox item version is required." });
            }

            request.UserId = userId;
            request.ItemId = ObjectId.GenerateNewId().ToString();
            // the API key's generation, pinned when it was checked
            request.Generation = await generations.GetAsync(userId);
            await inboxItemsRepository.InsertAsync(request);
            await syncDeviceService.AddIdsToAllDevicesAsync(userId, [new(request.ItemId, "inbox_item")]);
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
    }
}
