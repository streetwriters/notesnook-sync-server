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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notesnook.API.Interfaces;
using Notesnook.API.Models.Responses;
using Streetwriters.Common;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Models;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("users")]
    public class UsersController : ControllerBase
    {
        private readonly HttpClient httpClient;
        private readonly IHttpContextAccessor HttpContextAccessor;
        private IUserService UserService { get; set; }
        public UsersController(IUserService userService, IHttpContextAccessor accessor)
        {
            httpClient = new HttpClient();
            HttpContextAccessor = accessor;
            UserService = userService;
        }

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
            UserResponse response = await UserService.GetUserAsync();
            if (!response.Success) return BadRequest(response);
            return Ok(response);
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateUser([FromBody] UserResponse user)
        {
            UserResponse response = await UserService.GetUserAsync(false);

            if (user.AttachmentsKey != null)
                await UserService.SetUserAttachmentsKeyAsync(response.UserId, user.AttachmentsKey);

            return Ok();
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
        public async Task<IActionResult> Delete()
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");

                if (await UserService.DeleteUserAsync(userId, User.FindFirstValue("jti")))
                {
                    Response response = await this.httpClient.ForwardAsync<Response>(HttpContextAccessor, $"{Servers.IdentityServer}/account/unregister", HttpMethod.Post);
                    if (!response.Success) return BadRequest();

                    return Ok();
                }
                return BadRequest();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
