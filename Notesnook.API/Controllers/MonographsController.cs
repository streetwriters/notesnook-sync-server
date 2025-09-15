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
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Interfaces;
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
        private readonly IURLAnalyzer urlAnalyzer;
        private readonly IUnitOfWork unit;
        private const int MAX_DOC_SIZE = 15 * 1024 * 1024;
        public MonographsController(Repository<Monograph> monographs, IUnitOfWork unitOfWork, IURLAnalyzer analyzer)
        {
            Monographs = monographs;
            unit = unitOfWork;
            urlAnalyzer = analyzer;
        }

        private static FilterDefinition<Monograph> CreateMonographFilter(string userId, Monograph monograph)
        {
            var userIdFilter = Builders<Monograph>.Filter.Eq("UserId", userId);
            monograph.ItemId ??= monograph.Id;
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

                var existingMonograph = await FindMonographAsync(userId, monograph);
                if (existingMonograph != null && !existingMonograph.Deleted)
                {
                    return base.Conflict("This monograph is already published.");
                }

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = (await CleanupContentAsync(monograph.Content)).CompressBrotli();
                monograph.UserId = userId;
                monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                if (existingMonograph != null)
                {
                    monograph.Id = existingMonograph?.Id;
                }
                monograph.Deleted = false;
                await Monographs.Collection.ReplaceOneAsync(
                    CreateMonographFilter(userId, monograph),
                    monograph,
                    new ReplaceOptions { IsUpsert = true }
                );

                await MarkMonographForSyncAsync(monograph.ItemId ?? monograph.Id, deviceId);

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

                var existingMonograph = await FindMonographAsync(userId, monograph);
                if (existingMonograph == null || existingMonograph.Deleted)
                {
                    return NotFound();
                }

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = (await CleanupContentAsync(monograph.Content)).CompressBrotli();
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
                    .Set(m => m.Password, monograph.Password)
                );
                if (!result.IsAcknowledged) return BadRequest();

                await MarkMonographForSyncAsync(monograph.ItemId ?? monograph.Id, deviceId);

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

            var monographs = (await Monographs.Collection.FindAsync(
                    Builders<Monograph>.Filter.And(
                        Builders<Monograph>.Filter.Eq("UserId", userId),
                        Builders<Monograph>.Filter.Ne("Deleted", true)
                    )
               , new FindOptions<Monograph, ObjectWithId>
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
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted) return Content(SVG_PIXEL, "image/svg+xml");

            if (monograph.SelfDestruct)
            {
                var userId = this.User.FindFirstValue("sub");
                await Monographs.Collection.ReplaceOneAsync(
                    CreateMonographFilter(userId, monograph),
                    new Monograph
                    {
                        ItemId = id,
                        Id = monograph.Id,
                        Deleted = true,
                        UserId = monograph.UserId
                    }
                );

                await MarkMonographForSyncAsync(id);
            }

            return Content(SVG_PIXEL, "image/svg+xml");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync([FromQuery] string deviceId, [FromRoute] string id)
        {
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted)
            {
                return NotFound(new
                {
                    error = "invalid_id",
                    error_description = $"No such monograph found."
                });
            }

            var userId = this.User.FindFirstValue("sub");
            await Monographs.Collection.ReplaceOneAsync(
                CreateMonographFilter(userId, monograph),
                new Monograph
                {
                    ItemId = id,
                    Id = monograph.Id,
                    Deleted = true,
                    UserId = monograph.UserId
                }
            );

            await MarkMonographForSyncAsync(id, deviceId);

            return Ok();
        }

        private async Task MarkMonographForSyncAsync(string monographId, string deviceId)
        {
            if (deviceId == null) return;
            var userId = this.User.FindFirstValue("sub");

            new SyncDeviceService(new SyncDevice(userId, deviceId)).AddIdsToOtherDevices([$"{monographId}:monograph"]);
            await SendTriggerSyncEventAsync();
        }

        private async Task MarkMonographForSyncAsync(string monographId)
        {
            var userId = this.User.FindFirstValue("sub");

            new SyncDeviceService(new SyncDevice(userId, string.Empty)).AddIdsToAllDevices([$"{monographId}:monograph"]);
            await SendTriggerSyncEventAsync(sendToAllDevices: true);
        }

        private async Task SendTriggerSyncEventAsync(bool sendToAllDevices = false)
        {
            var userId = this.User.FindFirstValue("sub");
            var jti = this.User.FindFirstValue("jti");

            await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
            {
                OriginTokenId = sendToAllDevices ? null : jti,
                UserId = userId,
                Message = new Message
                {
                    Type = "triggerSync",
                    Data = JsonSerializer.Serialize(new { reason = "Monographs updated." })
                }
            });
        }

        private async Task<string> CleanupContentAsync(string content)
        {
            if (!Constants.IS_SELF_HOSTED && !ProUserRequirement.IsUserPro(User))
            {
                var config = Configuration.Default.WithDefaultLoader();
                var context = BrowsingContext.New(config);
                var document = await context.OpenAsync(r => r.Content(content));
                foreach (var element in document.QuerySelectorAll("a,iframe,img,object,svg,button,link"))
                {
                    element.Remove();
                }
                return document.ToHtml();
            }

            if (ProUserRequirement.IsUserPro(User))
            {
                var config = Configuration.Default.WithDefaultLoader();
                var context = BrowsingContext.New(config);
                var document = await context.OpenAsync(r => r.Content(content));
                foreach (var element in document.QuerySelectorAll("a"))
                {
                    var href = element.GetAttribute("href");
                    if (string.IsNullOrEmpty(href)) continue;
                    if (!await urlAnalyzer.IsURLSafeAsync(href)) element.RemoveAttribute("href");
                }
                return document.ToHtml();
            }
            return content;
        }
    }
}