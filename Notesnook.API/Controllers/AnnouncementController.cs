/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notesnook.API.Models;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{
    // TODO: this should be moved out into its own microservice
    [ApiController]
    [Route("announcements")]
    public class AnnouncementController : ControllerBase
    {
        private Repository<Announcement> Announcements { get; set; }
        public AnnouncementController(Repository<Announcement> announcements)
        {
            Announcements = announcements;
        }

        [HttpGet("active")]
        [AllowAnonymous]
        public async Task<IActionResult> GetActiveAnnouncements([FromQuery] string userId)
        {
            var announcements = await Announcements.FindAsync((a) => a.IsActive);
            return Ok(announcements.Where((a) => a.UserIds != null && a.UserIds.Length > 0
                                                    ? a.UserIds.Contains(userId)
                                                    : true));
        }
    }
}
