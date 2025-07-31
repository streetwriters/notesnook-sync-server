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
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Messages;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("monographs")]
    [Authorize("Sync")]
    public class MonographsController : ControllerBase
    {
        const string SVG_PIXEL = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><circle r='9'/></svg>";
        private Repository<Monograph> Monographs { get; set; }
        private readonly IUnitOfWork unit;
        private const int MAX_DOC_SIZE = 15 * 1024 * 1024;
        public MonographsController(Repository<Monograph> monographs, IUnitOfWork unitOfWork)
        {
            Monographs = monographs;
            unit = unitOfWork;
        }

        private static FilterDefinition<Monograph> CreateMonographFilter(string userId, Monograph monograph)
        {
            var userIdFilter = Builders<Monograph>.Filter.Eq("UserId", userId);
            return ObjectId.TryParse(monograph.ItemId, out ObjectId id)
            ? Builders<Monograph>.Filter
                .And(userIdFilter,
                    Builders<Monograph>.Filter.Or(
                        Builders<Monograph>.Filter.Eq("_id", id), Builders<Monograph>.Filter.Eq("ItemId", monograph.ItemId)
                    )
                )
            : Builders<Monograph>.Filter
                .And(userIdFilter,
                    Builders<Monograph>.Filter.Eq("ItemId", monograph.ItemId)
                );
        }

        private static FilterDefinition<Monograph> CreateMonographFilter(string itemId)
        {
            return ObjectId.TryParse(itemId, out ObjectId id)
            ? Builders<Monograph>.Filter.Or(
                Builders<Monograph>.Filter.Eq("_id", id),
                Builders<Monograph>.Filter.Eq("ItemId", itemId))
            : Builders<Monograph>.Filter.Eq("ItemId", itemId);
        }

        private async Task<Monograph> FindMonographAsync(string userId, Monograph monograph)
        {
            var result = await Monographs.Collection.FindAsync(CreateMonographFilter(userId, monograph), new FindOptions<Monograph>
            {
                Limit = 1
            });
            return await result.FirstOrDefaultAsync();
        }

        private async Task<Monograph> FindMonographAsync(string itemId)
        {
            var result = await Monographs.Collection.FindAsync(CreateMonographFilter(itemId), new FindOptions<Monograph>
            {
                Limit = 1
            });
            return await result.FirstOrDefaultAsync();
        }

        [HttpPost]
        public async Task<IActionResult> PublishAsync([FromQuery] string deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");
                if (userId == null) return Unauthorized();

                if (await FindMonographAsync(userId, monograph) != null) return base.Conflict("This monograph is already published.");

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = monograph.Content.CompressBrotli();
                monograph.UserId = userId;
                monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                await Monographs.InsertAsync(monograph);

                SyncMonograph(monograph.ItemId ?? monograph.Id, deviceId);

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

                if (await FindMonographAsync(userId, monograph) == null) return NotFound();

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = monograph.Content.CompressBrotli();
                else
                    monograph.Content = null;

                monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var result = await Monographs.Collection.UpdateOneAsync(
                    CreateMonographFilter(userId, monograph),
                    Builders<Monograph>.Update
                    .Set(m => m.DatePublished, monograph.DatePublished)
                    .Set(m => m.CompressedContent, monograph.CompressedContent)
                    .Set(m => m.EncryptedContent, monograph.EncryptedContent)
                    .Set(m => m.SelfDestruct, monograph.SelfDestruct)
                    .Set(m => m.Title, monograph.Title)
                );
                if (!result.IsAcknowledged) return BadRequest();


                SyncMonograph(monograph.ItemId ?? monograph.Id, deviceId);

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

            var monographs = (await Monographs.Collection.FindAsync(Builders<Monograph>.Filter.Eq("UserId", userId), new FindOptions<Monograph, ObjectWithId>
            {
                Projection = Builders<Monograph>.Projection.Include("_id").Include("ItemId"),
            })).ToEnumerable();
            return Ok(monographs.Select((m) => m.ItemId ?? m.Id));
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMonographAsync([FromRoute] string id)
        {
            var monograph = await FindMonographAsync(id);
            if (monograph == null)
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
            var monograph = await FindMonographAsync(id);
            if (monograph == null) return Content(SVG_PIXEL, "image/svg+xml");

            if (monograph.SelfDestruct)
            {
                var userId = this.User.FindFirstValue("sub");
                await Monographs.Collection.UpdateOneAsync(
                    CreateMonographFilter(userId, new Monograph { ItemId = id }),
                    Builders<Monograph>.Update
                    .Set(m => m.Deleted, true)
                );
                SyncMonograph(id, null);
            }

            return Content(SVG_PIXEL, "image/svg+xml");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync([FromQuery] string deviceId, [FromRoute] string id)
        {
            var userId = this.User.FindFirstValue("sub");
            await Monographs.Collection.UpdateOneAsync(
                    CreateMonographFilter(userId, new Monograph { ItemId = id }),
                    Builders<Monograph>.Update
                    .Set(m => m.Deleted, true)
            );
            SyncMonograph(id, deviceId);
            return Ok();
        }

        private void SyncMonograph(string monographId, string deviceId)
        {
            var userId = this.User.FindFirstValue("sub");
            var jti = this.User.FindFirstValue("jti");

            Task.Run(async () =>
            {
                if (deviceId == null)
                {
                    var emptyString = string.Empty;
                    await new SyncDeviceService(new SyncDevice(ref userId, ref emptyString)).AddIdsToAllDevicesAsync(new List<string> { $"{monographId}:monograph" });
                }
                else
                {
                    await new SyncDeviceService(new SyncDevice(ref userId, ref deviceId)).AddIdsToOtherDevicesAsync(new List<string> { $"{monographId}:monograph" });
                }

                await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
                {
                    SendToAll = true,
                    OriginTokenId = this.User.FindFirstValue("jti"),
                    UserId = userId,
                    Message = new Message
                    {
                        Type = "triggerSync",
                        Data = JsonSerializer.Serialize(new { reason = "Monographs updated." })
                    }
                });
            });
        }
    }
}