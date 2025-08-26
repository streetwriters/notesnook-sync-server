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
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("monographs")]
    [Authorize("Sync")]
    public class MonographsController : ControllerBase
    {
        const string SVG_PIXEL = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><circle r='9'/></svg>";
        private IMonographRepository Monographs { get; set; }
        private readonly ISyncDeviceServiceWrapper syncDeviceServiceWrapper;
        private readonly IMessengerService messengerService;
        private const int MAX_DOC_SIZE = 15 * 1024 * 1024;
        public MonographsController(IMonographRepository monographs, ISyncDeviceServiceWrapper syncDeviceServiceWrapper, IMessengerService messengerService)
        {
            Monographs = monographs;
            this.syncDeviceServiceWrapper = syncDeviceServiceWrapper;
            this.messengerService = messengerService;
        }

        [HttpPost]
        public async Task<IActionResult> PublishAsync([FromQuery] string deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");
                if (userId == null) return Unauthorized();

                var existingMonograph = await Monographs.FindByUserAndItemAsync(userId, monograph);
                if (existingMonograph != null && !existingMonograph.Deleted)
                {
                    return base.Conflict("This monograph is already published.");
                }

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = monograph.Content.CompressBrotli();
                monograph.UserId = userId;
                monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                if (existingMonograph != null)
                {
                    monograph.Id = existingMonograph?.Id;
                }
                monograph.Deleted = false;
                await Monographs.PublishOrUpdateAsync(userId, monograph);

                var jti = this.User.FindFirstValue("jti");
                await MarkMonographForSyncAsync(userId, jti, monograph.ItemId ?? monograph.Id, deviceId);

                return Ok(new
                {
                    id = monograph.ItemId,
                    datePublished = monograph.DatePublished,
                });
            }
            catch (Exception e)
            {
                await Slogger<MonographsController>.Error(nameof(PublishAsync), e.ToString());
                return BadRequest();
            }
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateAsync([FromQuery] string deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");
                if (userId == null) return Unauthorized();

                var existingMonograph = await Monographs.FindByUserAndItemAsync(userId, monograph);
                if (existingMonograph == null || existingMonograph.Deleted)
                {
                    return NotFound();
                }

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = monograph.Content.CompressBrotli();
                else
                    monograph.Content = null;

                monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var result = await Monographs.UpdateMonographAsync(userId, monograph);
                if (!result.IsAcknowledged) return BadRequest();

                var jti = this.User.FindFirstValue("jti");
                await MarkMonographForSyncAsync(userId, jti, monograph.ItemId ?? monograph.Id, deviceId);

                return Ok(new
                {
                    id = monograph.ItemId,
                    datePublished = monograph.DatePublished,
                });
            }
            catch (Exception e)
            {
                await Slogger<MonographsController>.Error(nameof(UpdateAsync), e.ToString());
                return BadRequest();
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserMonographsAsync()
        {
            var userId = this.User.FindFirstValue("sub");
            if (userId == null) return Unauthorized();

            var monographIds = await Monographs.GetUserMonographIdsAsync(userId);
            return Ok(monographIds);
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMonographAsync([FromRoute] string id)
        {
            var monograph = await Monographs.FindByItemIdAsync(id);
            if (monograph == null || monograph.Deleted)
            {
                return NotFound(new
                {
                    error = "invalid_id",
                    error_description = $"No such monograph found."
                });
            }

            if (monograph.EncryptedContent == null)
                monograph.Content = monograph.CompressedContent.DecompressBrotli();
            if (monograph.ItemId == null) monograph.ItemId = monograph.Id;
            return Ok(monograph);
        }

        [HttpGet("{id}/view")]
        [AllowAnonymous]
        public async Task<IActionResult> TrackView([FromRoute] string id)
        {
            var monograph = await Monographs.FindByItemIdAsync(id);
            if (monograph == null || monograph.Deleted) return Content(SVG_PIXEL, "image/svg+xml");

            if (monograph.SelfDestruct)
            {
                await Monographs.SelfDestructAsync(monograph, id);
                await MarkMonographForSyncAsync(monograph.UserId, id);
            }

            return Content(SVG_PIXEL, "image/svg+xml");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync([FromQuery] string deviceId, [FromRoute] string id)
        {
            var monograph = await Monographs.FindByItemIdAsync(id);
            if (monograph == null || monograph.Deleted)
            {
                return NotFound(new
                {
                    error = "invalid_id",
                    error_description = $"No such monograph found."
                });
            }

            var userId = this.User.FindFirstValue("sub");
            await Monographs.SoftDeleteAsync(userId, monograph, id);

            var jti = this.User.FindFirstValue("jti");
            await MarkMonographForSyncAsync(userId, jti, id, deviceId);

            return Ok();
        }

        private async Task MarkMonographForSyncAsync(string userId, string jti, string monographId, string deviceId)
        {
            if (deviceId == null) return;

            syncDeviceServiceWrapper.MarkMonographForSyncAsync(userId, monographId, deviceId);
            await messengerService.SendTriggerSyncEventAsync("Monographs updated", userId, jti, false);
        }

        private async Task MarkMonographForSyncAsync(string userId, string monographId)
        {
            syncDeviceServiceWrapper.MarkMonographForSyncAsync(userId, monographId);
            await messengerService.SendTriggerSyncEventAsync("Monographs updated", userId, null, true);
        }
    }
}