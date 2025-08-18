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
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AspNetCore.Identity.Mongo.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using Streetwriters.Identity.Services;
using static IdentityServer4.IdentityServerConstants;

namespace Streetwriters.Identity.Controllers
{
    [ApiController]
    [Route("mfa")]
    [Authorize(LocalApi.PolicyName)]
    public class MFAController : IdentityControllerBase
    {
        public MFAController(UserManager<User> _userManager, ITemplatedEmailSender _emailSender,
        SignInManager<User> _signInManager, RoleManager<MongoRole> _roleManager, IMFAService _mfaService) : base(_userManager, _emailSender, _signInManager, _roleManager, _mfaService) { }

        [HttpPost]
        public async Task<IActionResult> SetupAuthenticator([FromForm] MultiFactorSetupForm form)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User);

            try
            {
                switch (form.Type)
                {
                    case "app":
                        var authenticatorDetails = await MFAService.GetAuthenticatorDetailsAsync(user, client);
                        return Ok(authenticatorDetails);
                    case "sms":
                    case "email":
                        await MFAService.SendOTPAsync(user, client, form, true);
                        return Ok();
                    default:
                        return BadRequest("Invalid authenticator type.");
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete]
        public IActionResult Disable2FA()
        {
            return BadRequest("2FA is mandatory and cannot be disabled.");
        }

        [HttpGet("codes")]
        public async Task<IActionResult> GetRecoveryCodes()
        {
            var user = await UserManager.GetUserAsync(User);
            if (!await UserManager.GetTwoFactorEnabledAsync(user)) return BadRequest("Please enable 2FA.");
            return Ok(await UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 16));
        }

        [HttpPost("send")]
        [Authorize("mfa")]
        [Authorize(LocalApi.PolicyName)]
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> RequestCode([FromForm] string type)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.FindByIdAsync(User.FindFirstValue("sub"));
            if (user == null) return Ok(); // We cannot expose that the user doesn't exist.

            await MFAService.SendOTPAsync(user, client, new MultiFactorSetupForm
            {
                Type = type,
                PhoneNumber = user.PhoneNumber
            });
            return Ok();
        }

        [HttpPatch]
        public async Task<IActionResult> EnableAuthenticator([FromForm] MultiFactorEnableForm form)
        {
            var user = await UserManager.GetUserAsync(User);

            if (!await MFAService.VerifyOTPAsync(user, form.VerificationCode, form.Type))
                return BadRequest("Invalid verification code.");

            if (form.IsFallback)
                await MFAService.SetSecondaryMethodAsync(user, form.Type);
            else
                await MFAService.EnableMFAAsync(user, form.Type);

            return Ok();
        }

    }
}