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
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Models.Responses;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Messages;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("users")]
    public class UsersController(IUserService UserService, WampServiceAccessor serviceAccessor, ILogger<UsersController> logger) : ControllerBase
    {
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Signup()
        {
            try
            {
                await UserService.CreateUserAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sign up user");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUser()
        {
            var userId = User.GetUserId();
            try
            {
                UserResponse response = await UserService.GetUserAsync(userId);
                if (!response.Success) return BadRequest();
                return Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get user with id: {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateUser([FromBody] UserKeys keys)
        {
            var userId = User.GetUserId();
            try
            {
                await UserService.SetUserKeysAsync(userId, keys);
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update user with id: {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPatch("password/{type}")]
        public async Task<IActionResult> ChangePassword([FromRoute] string type, [FromBody] ChangePasswordForm form)
        {
            var userId = User.GetUserId();
            var clientId = User.FindFirstValue("client_id");
            var jti = User.FindFirstValue("jti");
            var isPasswordReset = type == "reset";
            try
            {
                var result = isPasswordReset ? await serviceAccessor.UserAccountService.ResetPasswordAsync(userId, form.NewPassword) : await serviceAccessor.UserAccountService.ChangePasswordAsync(userId, form.OldPassword, form.NewPassword);
                if (!result)
                    return BadRequest("Failed to change password.");

                await UserService.SetUserKeysAsync(userId, form.UserKeys);

                await serviceAccessor.UserAccountService.ClearSessionsAsync(userId, clientId, all: false, jti, null);

                await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
                {
                    UserId = userId,
                    OriginTokenId = User.FindFirstValue("jti"),
                    Message = new Message
                    {
                        Type = "logout",
                        Data = JsonSerializer.Serialize(new { reason = "Password changed." })
                    }
                });

                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to change password");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> Reset([FromForm] bool removeAttachments)
        {
            var userId = this.User.GetUserId();

            if (await UserService.ResetUserAsync(userId, removeAttachments))
                return Ok();
            return BadRequest();
        }

        [HttpPost("delete")]
        [RequestTimeout(5 * 60 * 1000)]
        public async Task<IActionResult> Delete([FromForm] DeleteAccountForm form)
        {
            var userId = this.User.GetUserId();
            var jti = User.FindFirstValue("jti");
            try
            {
                await UserService.DeleteUserAsync(userId, jti, form.Password);
                return Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete user with id: {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
