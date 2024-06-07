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
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Models;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("devices")]
    public class SyncDeviceController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> RegisterDevice([FromQuery] string deviceId)
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");
                new SyncDeviceService(new SyncDevice(ref userId, ref deviceId)).RegisterDevice();
                return Ok();
            }
            catch (Exception ex)
            {
                await Slogger<UsersController>.Error(nameof(UnregisterDevice), "Couldn't register device.", ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }


        [HttpDelete]
        public async Task<IActionResult> UnregisterDevice([FromQuery] string deviceId)
        {
            try
            {
                var userId = this.User.FindFirstValue("sub");
                new SyncDeviceService(new SyncDevice(ref userId, ref deviceId)).UnregisterDevice();
                return Ok();
            }
            catch (Exception ex)
            {
                await Slogger<UsersController>.Error(nameof(UnregisterDevice), "Couldn't unregister device.", ex.ToString());
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
