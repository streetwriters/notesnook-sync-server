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
using Microsoft.AspNetCore.Http.Extensions;

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
            var userId = this.User.GetUserId();

            var fileSize = HttpContext.Request.ContentLength ?? 0;
            bool hasBody = fileSize > 0;
            if (!hasBody) return Ok(Request.GetEncodedUrl());

            if (Constants.IS_SELF_HOSTED) await UploadFileAsync(userId, name, fileSize);
            else await UploadFileWithChecksAsync(userId, name, fileSize);

            return Ok();
        }

        private async Task UploadFileWithChecksAsync(string userId, string name, long fileSize)
        {
            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);

            var subscriptionService = await WampServers.SubscriptionServer.GetServiceAsync<IUserSubscriptionService>(SubscriptionServerTopics.UserSubscriptionServiceTopic);
            var subscription = await subscriptionService.GetUserSubscriptionAsync(Clients.Notesnook.Id, userId) ?? throw new Exception("User subscription not found.");

            if (StorageHelper.IsFileSizeExceeded(subscription, fileSize))
                throw new Exception("Max file size exceeded.");

            userSettings.StorageLimit ??= new Limit { Value = 0, UpdatedAt = 0 };
            if (StorageHelper.IsStorageLimitReached(subscription, userSettings.StorageLimit.Value + fileSize))
                throw new Exception("Storage limit exceeded.");

            var uploadedFileSize = await UploadFileAsync(userId, name, fileSize);

            userSettings.StorageLimit.Value += uploadedFileSize;
            userSettings.StorageLimit.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await Repositories.UsersSettings.UpsertAsync(userSettings, (u) => u.UserId == userId);

            // extra check in case user sets wrong ContentLength in the HTTP header
            if (uploadedFileSize != fileSize && StorageHelper.IsStorageLimitReached(subscription, userSettings.StorageLimit.Value))
            {
                await S3Service.DeleteObjectAsync(userId, name);
                throw new Exception("Storage limit exceeded.");
            }
        }

        private async Task<long> UploadFileAsync(string userId, string name, long fileSize)
        {
            var url = S3Service.GetInternalUploadObjectUrl(userId, name) ?? throw new Exception("Could not create signed url.");

            var httpClient = new HttpClient();
            var content = new StreamContent(HttpContext.Request.BodyReader.AsStream());
            content.Headers.ContentLength = fileSize;
            var response = await httpClient.SendRequestAsync<Response>(url, null, HttpMethod.Put, content);
            if (!response.Success) throw new Exception(response.Content != null ? await response.Content.ReadAsStringAsync() : "Could not upload file.");

            return await S3Service.GetObjectSizeAsync(userId, name);
        }


        [HttpGet("multipart")]
        public async Task<IActionResult> MultipartUpload([FromQuery] string name, [FromQuery] int parts, [FromQuery] string? uploadId)
        {
            var userId = this.User.GetUserId();
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
            var userId = this.User.GetUserId();
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
            var userId = this.User.GetUserId();
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
                var userId = this.User.GetUserId();
                var url = await S3Service.GetDownloadObjectUrl(userId, name);
                if (url == null) return BadRequest("Could not create signed url.");
                return Ok(url);
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpHead]
        public async Task<IActionResult> Info([FromQuery] string name)
        {
            var userId = this.User.GetUserId();
            var size = await S3Service.GetObjectSizeAsync(userId, name);
            HttpContext.Response.Headers.ContentLength = size;
            return Ok();
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAsync([FromQuery] string name)
        {
            try
            {
                var userId = this.User.GetUserId();
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
