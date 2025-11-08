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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("monographs")]
    [Authorize("Sync")]
    public class MonographsController(Repository<Monograph> monographs, IURLAnalyzer analyzer, ILogger<MonographsController> logger) : ControllerBase
    {
        const string SVG_PIXEL = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><circle r='9'/></svg>";
        private const int MAX_DOC_SIZE = 15 * 1024 * 1024;

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
            var result = await monographs.Collection.FindAsync(CreateMonographFilter(userId, monograph), new FindOptions<Monograph>
            {
                Limit = 1
            });
            return await result.FirstOrDefaultAsync();
        }

        private async Task<Monograph> FindMonographAsync(string itemId)
        {
            var result = await monographs.Collection.FindAsync(CreateMonographFilter(itemId), new FindOptions<Monograph>
            {
                Limit = 1
            });
            return await result.FirstOrDefaultAsync();
        }

        [HttpPost]
        public async Task<IActionResult> PublishAsync([FromQuery] string? deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.GetUserId();
                var jti = this.User.FindFirstValue("jti");

                var existingMonograph = await FindMonographAsync(userId, monograph);
                if (existingMonograph != null && !existingMonograph.Deleted) return await UpdateAsync(deviceId, monograph);

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = (await CleanupContentAsync(User, monograph.Content)).CompressBrotli();
                monograph.UserId = userId;
                monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                if (existingMonograph != null)
                {
                    monograph.Id = existingMonograph.Id;
                }
                monograph.Deleted = false;
                monograph.ViewCount = 0;
                await monographs.Collection.ReplaceOneAsync(
                    CreateMonographFilter(userId, monograph),
                    monograph,
                    new ReplaceOptions { IsUpsert = true }
                );

                await MarkMonographForSyncAsync(userId, monograph.ItemId ?? monograph.Id, deviceId, jti);

                return Ok(new
                {
                    id = monograph.ItemId,
                    datePublished = monograph.DatePublished
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to publish monograph");
                return BadRequest();
            }
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateAsync([FromQuery] string? deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.GetUserId();
                var jti = this.User.FindFirstValue("jti");

                var existingMonograph = await FindMonographAsync(userId, monograph);
                if (existingMonograph == null || existingMonograph.Deleted)
                {
                    return NotFound();
                }

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                if (monograph.EncryptedContent == null)
                    monograph.CompressedContent = (await CleanupContentAsync(User, monograph.Content)).CompressBrotli();
                else
                    monograph.Content = null;

                monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var result = await monographs.Collection.UpdateOneAsync(
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

                await MarkMonographForSyncAsync(userId, monograph.ItemId ?? monograph.Id, deviceId, jti);

                return Ok(new
                {
                    id = monograph.ItemId,
                    datePublished = monograph.DatePublished
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update monograph");
                return BadRequest();
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserMonographsAsync()
        {
            var userId = this.User.GetUserId();

            var userMonographs = (await monographs.Collection.FindAsync(
                    Builders<Monograph>.Filter.And(
                        Builders<Monograph>.Filter.Eq("UserId", userId),
                        Builders<Monograph>.Filter.Ne("Deleted", true)
                    )
               , new FindOptions<Monograph, ObjectWithId>
               {
                   Projection = Builders<Monograph>.Projection.Include("_id").Include("ItemId"),
               })).ToEnumerable();
            return Ok(userMonographs.Select((m) => m.ItemId ?? m.Id));
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
                monograph.Content = monograph.CompressedContent?.DecompressBrotli();
            monograph.ItemId ??= monograph.Id;
            return Ok(monograph);
        }

        [HttpGet("{id}/view")]
        [AllowAnonymous]
        public async Task<IActionResult> TrackView([FromRoute] string id)
        {
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted) return Content(SVG_PIXEL, "image/svg+xml");

            var cookieName = $"viewed_{id}";
            var hasVisitedBefore = Request.Cookies.ContainsKey(cookieName);

            if (monograph.SelfDestruct)
            {
                await monographs.Collection.ReplaceOneAsync(
                    CreateMonographFilter(monograph.UserId, monograph),
                    new Monograph
                    {
                        ItemId = id,
                        Id = monograph.Id,
                        Deleted = true,
                        UserId = monograph.UserId,
                        ViewCount = 0
                    }
                );
                await MarkMonographForSyncAsync(monograph.UserId, id);
            }
            else if (!hasVisitedBefore)
            {
                await monographs.Collection.UpdateOneAsync(
                    CreateMonographFilter(monograph.UserId, monograph),
                    Builders<Monograph>.Update.Inc(m => m.ViewCount, 1)
                );

                var cookieOptions = new CookieOptions
                {
                    Path = $"/monographs/{id}",
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddMonths(1)
                };
                Response.Cookies.Append(cookieName, "1", cookieOptions);
            }

            return Content(SVG_PIXEL, "image/svg+xml");
        }

        [HttpGet("{id}/analytics")]
        public async Task<IActionResult> GetMonographAnalyticsAsync([FromRoute] string id)
        {
            if (!FeatureAuthorizationHelper.IsFeatureAllowed(Features.MONOGRAPH_ANALYTICS, Clients.Notesnook.Id, User))
                return BadRequest(new { error = "Monograph analytics are only available on the Pro & Believer plans." });

            var userId = this.User.GetUserId();
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted || monograph.UserId != userId)
            {
                return NotFound();
            }

            return Ok(new { totalViews = monograph.ViewCount });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync([FromQuery] string? deviceId, [FromRoute] string id)
        {
            var userId = this.User.GetUserId();

            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted)
                return Ok();

            var jti = this.User.FindFirstValue("jti");

            await monographs.Collection.ReplaceOneAsync(
                CreateMonographFilter(userId, monograph),
                new Monograph
                {
                    ItemId = id,
                    Id = monograph.Id,
                    Deleted = true,
                    UserId = monograph.UserId,
                    ViewCount = 0
                }
            );

            await MarkMonographForSyncAsync(userId, id, deviceId, jti);

            return Ok();
        }

        private static async Task MarkMonographForSyncAsync(string userId, string monographId, string? deviceId, string? jti)
        {
            if (deviceId == null) return;

            new SyncDeviceService(new SyncDevice(userId, deviceId)).AddIdsToOtherDevices([$"{monographId}:monograph"]);
            await SendTriggerSyncEventAsync(userId, jti);
        }

        private static async Task MarkMonographForSyncAsync(string userId, string monographId)
        {
            new SyncDeviceService(new SyncDevice(userId, string.Empty)).AddIdsToAllDevices([$"{monographId}:monograph"]);
            await SendTriggerSyncEventAsync(userId, sendToAllDevices: true);
        }

        private static async Task SendTriggerSyncEventAsync(string userId, string? jti = null, bool sendToAllDevices = false)
        {
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

        private async Task<string> CleanupContentAsync(ClaimsPrincipal user, string? content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            if (Constants.IS_SELF_HOSTED) return content;
            try
            {
                var json = JsonSerializer.Deserialize<MonographContent>(content) ?? throw new Exception("Invalid monograph content.");
                var html = json.Data;

                if (user.IsUserSubscribed())
                {
                    var config = Configuration.Default.WithDefaultLoader();
                    var context = BrowsingContext.New(config);
                    var document = await context.OpenAsync(r => r.Content(html));
                    foreach (var element in document.QuerySelectorAll("a"))
                    {
                        var href = element.GetAttribute("href");
                        if (string.IsNullOrEmpty(href)) continue;
                        if (!await analyzer.IsURLSafeAsync(href))
                        {
                            logger.LogInformation("Malicious URL detected: {Url}", href);
                            element.RemoveAttribute("href");
                        }
                    }
                    html = document.ToHtml();
                }
                else
                {
                    var config = Configuration.Default.WithDefaultLoader();
                    var context = BrowsingContext.New(config);
                    var document = await context.OpenAsync(r => r.Content(html));
                    foreach (var element in document.QuerySelectorAll("a,iframe,img,object,svg,button,link"))
                    {
                        foreach (var attr in element.Attributes)
                            element.RemoveAttribute(attr.Name);
                    }
                    html = document.ToHtml();
                }

                return JsonSerializer.Serialize<MonographContent>(new MonographContent
                {
                    Type = json.Type,
                    Data = html
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to cleanup monograph content");
                return content;
            }
        }
    }
}