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
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Notesnook.API.Helpers;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Models;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("s3")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize("Sync")]
    public class S3Controller(IS3Service s3Service, ISyncItemsRepositoryAccessor repositories, WampServiceAccessor serviceAccessor, ILogger<S3Controller> logger) : ControllerBase
    {
        [HttpPut]
        public async Task<IActionResult> Upload([FromQuery] string name)
        {
            try
            {
                var userId = this.User.GetUserId();

                var fileSize = HttpContext.Request.ContentLength ?? 0;
                bool hasBody = fileSize > 0;

                if (!hasBody)
                {
                    return Ok(Request.GetEncodedUrl() + "&access_token=" + Request.Headers.Authorization.ToString().Replace("Bearer ", ""));
                }

                if (Constants.IS_SELF_HOSTED) await UploadFileAsync(userId, name, fileSize);
                else await UploadFileWithChecksAsync(userId, name, fileSize);

                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error uploading attachment for user.");
                return BadRequest(new { error = "Failed to upload attachment." });
            }
        }

        private async Task UploadFileWithChecksAsync(string userId, string name, long fileSize)
        {
            var userSettings = await repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);

            var subscription = await serviceAccessor.UserSubscriptionService.GetUserSubscriptionAsync(Clients.Notesnook.Id, userId) ?? throw new Exception("User subscription not found.");

            if (StorageHelper.IsFileSizeExceeded(subscription, fileSize))
                throw new Exception("Max file size exceeded.");

            userSettings.StorageLimit = StorageHelper.RolloverStorageLimit(userSettings.StorageLimit);
            if (StorageHelper.IsStorageLimitReached(subscription, userSettings.StorageLimit.Value + fileSize))
                throw new Exception("Storage limit exceeded.");

            var uploadedFileSize = await UploadFileAsync(userId, name, fileSize);

            userSettings.StorageLimit.Value += uploadedFileSize;
            await repositories.UsersSettings.Collection.UpdateOneAsync(
                Builders<UserSettings>.Filter.Eq(u => u.UserId, userId),
                Builders<UserSettings>.Update.Set(u => u.StorageLimit, userSettings.StorageLimit)
            );

            // extra check in case user sets wrong ContentLength in the HTTP header
            if (uploadedFileSize != fileSize && StorageHelper.IsStorageLimitReached(subscription, userSettings.StorageLimit.Value))
            {
                await s3Service.DeleteObjectAsync(userId, name);
                throw new Exception("Storage limit exceeded.");
            }
        }

        private async Task<long> UploadFileAsync(string userId, string name, long fileSize)
        {
            var url = await s3Service.GetInternalUploadObjectUrlAsync(userId, name) ?? throw new Exception("Could not create signed url.");

            var httpClient = new HttpClient();
            var content = new StreamContent(HttpContext.Request.BodyReader.AsStream());
            content.Headers.ContentLength = fileSize;
            var response = await httpClient.SendRequestAsync<Response>(url, null, HttpMethod.Put, content);
            if (!response.Success) throw new Exception(response.Content != null ? await response.Content.ReadAsStringAsync() : "Could not upload file.");

            return await s3Service.GetObjectSizeAsync(userId, name);
        }


        [HttpGet("multipart")]
        public async Task<IActionResult> MultipartUpload([FromQuery] string name, [FromQuery] int parts, [FromQuery] string? uploadId)
        {
            var userId = this.User.GetUserId();
            try
            {
                var meta = await s3Service.StartMultipartUploadAsync(userId, name, parts, uploadId);
                return Ok(meta);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error starting multipart upload for user.");
                return BadRequest(new { error = "Failed to start multipart upload." });
            }
        }

        [HttpDelete("multipart")]
        public async Task<IActionResult> AbortMultipartUpload([FromQuery] string name, [FromQuery] string uploadId)
        {
            var userId = this.User.GetUserId();
            try
            {
                await s3Service.AbortMultipartUploadAsync(userId, name, uploadId);
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error aborting multipart upload for user.");
                return BadRequest(new { error = "Failed to abort multipart upload." });
            }
        }

        [HttpPost("multipart")]
        public async Task<IActionResult> CompleteMultipartUpload([FromBody] CompleteMultipartUploadRequestWrapper uploadRequestWrapper)
        {
            var userId = this.User.GetUserId();
            try
            {
                await s3Service.CompleteMultipartUploadAsync(userId, uploadRequestWrapper.ToRequest());
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error completing multipart upload for user.");
                return BadRequest(new { error = "Failed to complete multipart upload." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Download([FromQuery] string name)
        {
            try
            {
                var userId = this.User.GetUserId();
                var url = await s3Service.GetDownloadObjectUrlAsync(userId, name);
                if (url == null) return BadRequest("Could not create signed url.");
                return Ok(url);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating download url for user.");
                return BadRequest(new { error = "Failed to get attachment url." });
            }
        }

        [HttpHead]
        public async Task<IActionResult> Info([FromQuery] string name)
        {
            try
            {
                var userId = this.User.GetUserId();
                var size = await s3Service.GetObjectSizeAsync(userId, name);
                HttpContext.Response.Headers.ContentLength = size;
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting object info for user.");
                return BadRequest(new { error = "Failed to get attachment info." });
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAsync([FromQuery] string name)
        {
            try
            {
                var userId = this.User.GetUserId();
                await s3Service.DeleteObjectAsync(userId, name);
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting object for user.");
                return BadRequest(new { error = "Failed to delete attachment." });
            }
        }

        [HttpPost("bulk-delete")]
        public async Task<IActionResult> DeleteBulkAsync([FromBody] DeleteBulkObjectsRequest request)
        {
            try
            {
                if (request.Names == null || request.Names.Length == 0)
                {
                    return BadRequest(new { error = "No files specified for deletion." });
                }

                var userId = this.User.GetUserId();
                await s3Service.DeleteObjectsAsync(userId, request.Names);
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting objects for user.");
                return BadRequest(new { error = "Failed to delete attachments." });
            }
        }
    }
}
