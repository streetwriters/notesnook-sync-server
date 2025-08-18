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
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Models.Responses;
using Streetwriters.Common;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("users")]
    public class UsersController(IUserService UserService) : ControllerBase
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
                await Slogger<UsersController>.Error(nameof(Signup), "Couldn't sign up.", ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUser()
        {
            var userId = User.FindFirstValue("sub");
            try
            {
                UserResponse response = await UserService.GetUserAsync(userId);
                if (!response.Success) return BadRequest(response);
                return Ok(response);
            }
            catch (Exception ex)
            {
                await Slogger<UsersController>.Error(nameof(GetUser), "Couldn't get user for id.", userId, ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateUser([FromBody] UserResponse user)
        {
            var userId = User.FindFirstValue("sub");
            try
            {
                var keys = new UserKeys
                {
                    AttachmentsKey = user.AttachmentsKey,
                    MonographPasswordsKey = user.MonographPasswordsKey
                };
                await UserService.SetUserKeysAsync(userId, keys);
                return Ok();
            }
            catch (Exception ex)
            {
                await Slogger<UsersController>.Error(nameof(GetUser), "Couldn't update user with id.", userId, ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> Reset([FromForm] bool removeAttachments)
        {
            var userId = this.User.FindFirstValue("sub");

            if (await UserService.ResetUserAsync(userId, removeAttachments))
                return Ok();
            return BadRequest();
        }

        [HttpPost("delete")]
        [RequestTimeout(5 * 60 * 1000)]
        public async Task<IActionResult> Delete([FromForm] DeleteAccountForm form)
        {
            var userId = this.User.FindFirstValue("sub");
            var jti = User.FindFirstValue("jti");
            try
            {
                await UserService.DeleteUserAsync(userId, jti, form.Password);
                return Ok();
            }
            catch (Exception ex)
            {
                await Slogger<UsersController>.Error(nameof(GetUser), "Couldn't delete user with id.", userId, ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
