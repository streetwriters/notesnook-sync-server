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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Amazon.S3.Model;
using System.Threading.Tasks;
using System.Security.Claims;
using Notesnook.API.Interfaces;
using System;
using System.Net.Http;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Models;
using Notesnook.API.Helpers;
using Streetwriters.Common;
using Streetwriters.Common.Interfaces;
using Notesnook.API.Models;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("s3")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize("Sync")]
    public class S3Controller : ControllerBase
    {
        private ISyncItemsRepositoryAccessor Repositories { get; }
        private IS3Service S3Service { get; set; }
        public S3Controller(IS3Service s3Service, ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor)
        {
            S3Service = s3Service;
            Repositories = syncItemsRepositoryAccessor;
        }

        [HttpPut]
        public async Task<IActionResult> Upload([FromQuery] string name)
        {
            var userId = this.User.FindFirstValue("sub");

            if (!HttpContext.Request.Headers.ContentLength.HasValue) return BadRequest(new { error = "No Content-Length header found." });

            long fileSize = HttpContext.Request.Headers.ContentLength.Value;
            if (fileSize == 0)
            {
                var uploadUrl = S3Service.GetUploadObjectUrl(userId, name);
                if (uploadUrl == null) return BadRequest(new { error = "Could not create signed url." });
                return Ok(uploadUrl);
            }

            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);
            if (!Constants.IS_SELF_HOSTED)
            {
                var subscriptionService = await WampServers.SubscriptionServer.GetServiceAsync<IUserSubscriptionService>(SubscriptionServerTopics.UserSubscriptionServiceTopic);
                var subscription = await subscriptionService.GetUserSubscriptionAsync(Clients.Notesnook.Id, userId);
                if (subscription is null) return BadRequest(new { error = "User subscription not found." });

                if (StorageHelper.IsFileSizeExceeded(subscription, fileSize))
                {
                    return BadRequest(new { error = "Max file size exceeded." });
                }

                userSettings.StorageLimit ??= new Limit { Value = 0, UpdatedAt = 0 };
                userSettings.StorageLimit.Value += fileSize;
                if (StorageHelper.IsStorageLimitReached(subscription, userSettings.StorageLimit))
                    return BadRequest(new { error = "Storage limit exceeded." });
            }

            var url = S3Service.GetUploadObjectUrl(userId, name);
            if (url == null) return BadRequest(new { error = "Could not create signed url." });

            var httpClient = new HttpClient();
            var content = new StreamContent(HttpContext.Request.BodyReader.AsStream());
            content.Headers.ContentLength = Request.ContentLength;
            var response = await httpClient.SendRequestAsync<Response>(url, null, HttpMethod.Put, content);
            if (!response.Success) return BadRequest(await response.Content.ReadAsStringAsync());

            if (!Constants.IS_SELF_HOSTED)
            {
                userSettings.StorageLimit.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await Repositories.UsersSettings.UpsertAsync(userSettings, (u) => u.UserId == userId);
            }

            return Ok(response);
        }


        [HttpGet("multipart")]
        public async Task<IActionResult> MultipartUpload([FromQuery] string name, [FromQuery] int parts, [FromQuery] string? uploadId)
        {
            var userId = this.User.FindFirstValue("sub");
            try
            {
                var meta = await S3Service.StartMultipartUploadAsync(userId, name, parts, uploadId);
                return Ok(meta);
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpDelete("multipart")]
        public async Task<IActionResult> AbortMultipartUpload([FromQuery] string name, [FromQuery] string uploadId)
        {
            var userId = this.User.FindFirstValue("sub");
            try
            {
                await S3Service.AbortMultipartUploadAsync(userId, name, uploadId);
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("multipart")]
        public async Task<IActionResult> CompleteMultipartUpload([FromBody] CompleteMultipartUploadRequestWrapper uploadRequestWrapper)
        {
            var userId = this.User.FindFirstValue("sub");
            try
            {
                await S3Service.CompleteMultipartUploadAsync(userId, uploadRequestWrapper.ToRequest());
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpGet]
        public async Task<IActionResult> Download([FromQuery] string name)
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");
                var url = await S3Service.GetDownloadObjectUrl(userId, name);
                if (url == null) return BadRequest("Could not create signed url.");
                return Ok(url);
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpHead]
        public async Task<IActionResult> Info([FromQuery] string name)
        {
            var userId = this.User.FindFirstValue("sub");
            var size = await S3Service.GetObjectSizeAsync(userId, name);
            HttpContext.Response.Headers.ContentLength = size;
            return Ok();
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAsync([FromQuery] string name)
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");
                await S3Service.DeleteObjectAsync(userId, name);
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
