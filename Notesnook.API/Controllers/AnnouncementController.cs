using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notesnook.API.Models;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{

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
