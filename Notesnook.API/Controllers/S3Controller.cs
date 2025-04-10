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

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("s3")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class S3Controller : ControllerBase
    {
        private IS3Service S3Service { get; set; }
        public S3Controller(IS3Service s3Service)
        {
            S3Service = s3Service;
        }

        [HttpPut]
        [Authorize("Pro")]
        public IActionResult Upload([FromQuery] string name)
        {
            var userId = this.User.FindFirstValue("sub");
            var url = S3Service.GetUploadObjectUrl(userId, name);
            if (url == null) return BadRequest("Could not create signed url.");
            return Ok(url);
        }


        [HttpGet("multipart")]
        [Authorize("Pro")]
        public async Task<IActionResult> MultipartUpload([FromQuery] string name, [FromQuery] int parts, [FromQuery] string uploadId)
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
        [Authorize("Pro")]
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
        [Authorize("Pro")]
        public async Task<IActionResult> CompleteMultipartUpload([FromBody] CompleteMultipartUploadRequest uploadRequest)
        {
            var userId = this.User.FindFirstValue("sub");
            try
            {
                await S3Service.CompleteMultipartUploadAsync(userId, uploadRequest);
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpGet]
        [Authorize("Sync")]
        public IActionResult Download([FromQuery] string name)
        {
            var userId = this.User.FindFirstValue("sub");
            var url = S3Service.GetDownloadObjectUrl(userId, name);
            if (url == null) return BadRequest("Could not create signed url.");
            return Ok(url);
        }

        [HttpHead]
        [Authorize("Sync")]
        public async Task<IActionResult> Info([FromQuery] string name)
        {
            var userId = this.User.FindFirstValue("sub");
            var size = await S3Service.GetObjectSizeAsync(userId, name);
            HttpContext.Response.Headers.ContentLength = size;
            return Ok();
        }

        [HttpDelete]
        [Authorize("Sync")]
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
