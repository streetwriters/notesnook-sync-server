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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Notesnook.API.Accessors;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{
    // TODO: this should be moved out into its own microservice
    [ApiController]
    [Route("announcements")]
    public class AnnouncementController(Repository<Announcement> announcements, WampServiceAccessor serviceAccessor) : ControllerBase
    {
        [HttpGet("active")]
        [AllowAnonymous]
        public async Task<IActionResult> GetActiveAnnouncements([FromQuery] string? userId)
        {
            var filter = Builders<Announcement>.Filter.Eq(x => x.IsActive, true);
            if (!string.IsNullOrEmpty(userId))
            {
                var userFilter = Builders<Announcement>.Filter.Or(
                    Builders<Announcement>.Filter.Eq(x => x.UserIds, null),
                    Builders<Announcement>.Filter.Size(x => x.UserIds, 0),
                    Builders<Announcement>.Filter.AnyEq(x => x.UserIds, userId)
                );
                filter = Builders<Announcement>.Filter.And(filter, userFilter);
            }
            var userAnnouncements = await announcements.Collection.Find(filter).ToListAsync();
            foreach (var announcement in userAnnouncements)
            {
                if (userId != null && announcement.UserIds != null && !announcement.UserIds.Contains(userId)) continue;

                foreach (var item in announcement.Body)
                {
                    if (item.Type != "callToActions") continue;
                    foreach (var action in item.Actions)
                    {
                        if (action.Type != "link" || action.Data == null) continue;

                        action.Data = action.Data.Replace("{{UserId}}", userId ?? "");

                        if (action.Data.Contains("{{Email}}"))
                        {
                            var user = string.IsNullOrEmpty(userId) ? null : await serviceAccessor.UserAccountService.GetUserAsync(Clients.Notesnook.Id, userId);
                            action.Data = action.Data.Replace("{{Email}}", user?.Email ?? "");
                        }
                    }
                }
            }
            return Ok(userAnnouncements);
        }
    }
}
