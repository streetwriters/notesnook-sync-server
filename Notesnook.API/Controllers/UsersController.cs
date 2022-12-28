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
            else return BadRequest();

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

                Response response = await this.httpClient.ForwardAsync<Response>(this.HttpContextAccessor, $"{Servers.IdentityServer.ToString()}/account/unregister", HttpMethod.Post);
                if (!response.Success) return BadRequest();

                if (await UserService.DeleteUserAsync(userId, User.FindFirstValue("jti")))
                    return Ok();

                return BadRequest();
            }
            catch
            {
                return BadRequest();
            }
        }
    }
}
