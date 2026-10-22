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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Notesnook.API.Helpers;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Exceptions;
using Streetwriters.Common.Extensions;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("s3")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Authorize("Sync")]
    public class S3Controller(IS3Service s3Service, ISyncItemsRepositoryAccessor repositories, WampServiceAccessor serviceAccessor, IHttpClientFactory httpClientFactory) : ControllerBase
    {
        [HttpPut]
        [EnableRateLimiting("s3-direct")]
        public async Task<IActionResult> Upload([FromQuery] string name)
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

        private async Task UploadFileWithChecksAsync(string userId, string name, long fileSize)
        {
            var subscription = await serviceAccessor.UserSubscriptionService.GetUserSubscriptionAsync(Clients.Notesnook.Id, userId) ?? throw new ClientException("User subscription not found.");

            if (StorageHelper.IsFileSizeExceeded(subscription, fileSize))
                throw new ClientException("Max file size exceeded.");

            if (!await s3Service.TryReserveStorageAsync(userId, fileSize, StorageHelper.GetStorageLimitForPlan(subscription)))
                throw new ClientException("Storage limit exceeded.");

            try
            {
                await UploadFileAsync(userId, name, fileSize);
            }
            catch
            {
                await s3Service.ReleaseStorageAsync(userId, fileSize);
                throw;
            }
        }

        private async Task<long> UploadFileAsync(string userId, string name, long fileSize)
        {
            var url = await s3Service.GetInternalUploadObjectUrlAsync(userId, name) ?? throw new Exception("Could not create signed url.");

            var httpClient = httpClientFactory.CreateClient("S3Upload");
            var content = new StreamContent(HttpContext.Request.BodyReader.AsStream());
            content.Headers.ContentLength = fileSize;
            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = content
            };
            using var uploadTimeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            uploadTimeout.CancelAfter(TimeSpan.FromMinutes(15 + (15 * fileSize / (1024d * 1024d * 1024d))));
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                uploadTimeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                if (S3TransientErrorClassifier.IsTransient(statusCode))
                    throw new S3StorageUnavailableException("Attachment storage is temporarily unavailable. Please try again later.");

                throw new Exception(await response.Content.ReadAsStringAsync(uploadTimeout.Token));
            }

            // The PUT response confirms this request. A follow-up HEAD can lag or
            // be throttled, so direct accounting uses the accepted request bytes.
            return fileSize;
        }


        [HttpGet("multipart")]
        [EnableRateLimiting("s3-multipart-control")]
        public async Task<IActionResult> MultipartUpload([FromQuery] string name, [FromQuery] int parts, [FromQuery] string? uploadId)
        {
            var userId = this.User.GetUserId();
            var meta = await s3Service.StartMultipartUploadAsync(userId, name, parts, uploadId);
            return Ok(meta);
        }

        [HttpDelete("multipart")]
        public async Task<IActionResult> AbortMultipartUpload([FromQuery] string name, [FromQuery] string uploadId)
        {
            var userId = this.User.GetUserId();
            await s3Service.AbortMultipartUploadAsync(userId, name, uploadId);
            return Ok();
        }

        [HttpPost("multipart")]
        [EnableRateLimiting("s3-multipart-control")]
        public async Task<IActionResult> CompleteMultipartUpload([FromBody] CompleteMultipartUploadRequestWrapper uploadRequestWrapper)
        {
            var userId = this.User.GetUserId();
            await s3Service.CompleteMultipartUploadAsync(userId, uploadRequestWrapper.ToRequest());
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> Download([FromQuery] string name)
        {
            var userId = this.User.GetUserId();
            var url = await s3Service.GetDownloadObjectUrlAsync(userId, name);
            if (url == null) return BadRequest("Could not create signed url.");
            return Ok(url);
        }

        [HttpHead]
        public async Task<IActionResult> Info([FromQuery] string name)
        {
            var userId = this.User.GetUserId();
            var size = await s3Service.GetObjectSizeAsync(userId, name); Response.Headers.ContentLength = size;
            Response.Headers["X-Object-Size"] = size.ToString();
            return Ok();
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAsync([FromQuery] string name)
        {
            var userId = this.User.GetUserId();
            await s3Service.DeleteObjectAsync(userId, name);
            return Ok();
        }

        // [HttpPost("bulk-delete")]
        // public async Task<IActionResult> DeleteBulkAsync([FromBody] DeleteBulkObjectsRequest request)
        // {
        //     try
        //     {
        //         if (request.Names == null || request.Names.Length == 0)
        //         {
        //             return BadRequest(new { error = "No files specified for deletion." });
        //         }

        //         var userId = this.User.GetUserId();
        //         await s3Service.DeleteObjectsAsync(userId, request.Names);
        //         return Ok();
        //     }
        //     catch (Exception ex)
        //     {
        //         logger.LogError(ex, "Error deleting objects for user.");
        //         return BadRequest(new { error = "Failed to delete attachments." });
        //     }
        // }
    }
}
