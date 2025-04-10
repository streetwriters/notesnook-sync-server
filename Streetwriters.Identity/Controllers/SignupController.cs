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

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AspNetCore.Identity.Mongo.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Enums;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Controllers
{
    [ApiController]
    [Route("signup")]
    public class SignupController : IdentityControllerBase
    {
        public SignupController(UserManager<User> _userManager, ITemplatedEmailSender _emailSender,
        SignInManager<User> _signInManager, RoleManager<MongoRole> _roleManager, IMFAService _mfaService) : base(_userManager, _emailSender, _signInManager, _roleManager, _mfaService)
        { }

        private async Task AddClientRoleAsync(string clientId)
        {
            if (await RoleManager.FindByNameAsync(clientId) == null)
                await RoleManager.CreateAsync(new MongoRole(clientId));
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Signup([FromForm] SignupForm form)
        {
            if (Constants.DISABLE_SIGNUPS)
                return BadRequest(new string[] { "Creating new accounts is not allowed." });
            try
            {
                var client = Clients.FindClientById(form.ClientId);
                if (client == null) return BadRequest(new string[] { "Invalid client id." });

                await AddClientRoleAsync(client.Id);

                // email addresses must be case-insensitive
                form.Email = form.Email.ToLowerInvariant();
                form.Username = form.Username?.ToLowerInvariant();

                if (!await EmailAddressValidator.IsEmailAddressValidAsync(form.Email)) return BadRequest(new string[] { "Invalid email address." });

                var result = await UserManager.CreateAsync(new User
                {
                    Email = form.Email,
                    EmailConfirmed = Constants.IS_SELF_HOSTED,
                    UserName = form.Username ?? form.Email,
                }, form.Password);

                if (result.Errors.Any((e) => e.Code == "DuplicateEmail"))
                {
                    var user = await UserManager.FindByEmailAsync(form.Email);

                    if (!await UserManager.IsInRoleAsync(user, client.Id))
                    {
                        if (!await UserManager.CheckPasswordAsync(user, form.Password))
                        {
                            // TODO
                            await UserManager.RemovePasswordAsync(user);
                            await UserManager.AddPasswordAsync(user, form.Password);
                        }
                        await MFAService.DisableMFAAsync(user);
                        await UserManager.AddToRoleAsync(user, client.Id);
                    }
                    else
                    {
                        return BadRequest(new string[] { "Invalid email address.." });
                    }

                    return Ok(new
                    {
                        userId = user.Id.ToString()
                    });
                }

                if (result.Succeeded)
                {
                    var user = await UserManager.FindByEmailAsync(form.Email);
                    await UserManager.AddToRoleAsync(user, client.Id);
                    if (Constants.IS_SELF_HOSTED)
                    {
                        await UserManager.AddClaimAsync(user, UserService.SubscriptionTypeToClaim(client.Id, Common.Enums.SubscriptionType.PREMIUM));
                    }
                    else
                    {
                        await UserManager.AddClaimAsync(user, new Claim("platform", PlatformFromUserAgent(base.HttpContext.Request.Headers.UserAgent)));
                        var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
                        var callbackUrl = Url.TokenLink(user.Id.ToString(), code, client.Id, TokenType.CONFRIM_EMAIL);
                        await EmailSender.SendConfirmationEmailAsync(user.Email, callbackUrl, client);
                    }
                    return Ok(new
                    {
                        userId = user.Id.ToString()
                    });
                }

                return BadRequest(result.Errors.ToErrors());
            }
            catch (System.Exception ex)
            {
                await Slogger<SignupController>.Error("Signup", ex.ToString());
                return BadRequest("Failed to create an account.");
            }
        }

        string PlatformFromUserAgent(string userAgent)
        {
            return userAgent.Contains("okhttp/") ? "android" : userAgent.Contains("Darwin/") || userAgent.Contains("CFNetwork/") ? "ios" : "web";
        }
    }
}
