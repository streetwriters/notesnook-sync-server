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

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AspNetCore.Identity.Mongo.Model;
using IdentityServer4.Configuration;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Streetwriters.Common;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Enums;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using static IdentityServer4.IdentityServerConstants;

namespace Streetwriters.Identity.Controllers
{
    [ApiController]
    [DisplayName("Account")]
    [Route("account")]
    [Authorize(LocalApi.PolicyName)]
    public class AccountController : IdentityControllerBase
    {
        private IPersistedGrantStore PersistedGrantStore { get; set; }
        private ITokenGenerationService TokenGenerationService { get; set; }
        private IUserClaimsPrincipalFactory<User> PrincipalFactory { get; set; }
        private IdentityServerOptions ISOptions { get; set; }
        public AccountController(UserManager<User> _userManager, IEmailSender _emailSender,
        SignInManager<User> _signInManager, RoleManager<MongoRole> _roleManager, IPersistedGrantStore store,
        ITokenGenerationService tokenGenerationService, IMFAService _mfaService) : base(_userManager, _emailSender, _signInManager, _roleManager, _mfaService)
        {
            PersistedGrantStore = store;
            TokenGenerationService = tokenGenerationService;
        }

        [HttpGet("confirm")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> ConfirmToken(string userId, string code, string clientId, TokenType type)
        {
            var client = Clients.FindClientById(clientId);
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.FindByIdAsync(userId);
            if (!await IsUserValidAsync(user, clientId)) return BadRequest($"Unable to find user with ID '{userId}'.");

            switch (type)
            {
                case TokenType.CONFRIM_EMAIL:
                    {
                        if (await UserManager.IsEmailConfirmedAsync(user)) return Ok("Email already verified.");

                        var result = await UserManager.ConfirmEmailAsync(user, code);
                        if (!result.Succeeded) return BadRequest(result.Errors.ToErrors());


                        if (await UserManager.IsInRoleAsync(user, client.Id))
                        {
                            await client.OnEmailConfirmed(userId);
                            // if (client.WelcomeEmailTemplateId != null)
                            //     await EmailSender.SendWelcomeEmailAsync(user.Email, client);
                        }

                        var redirectUrl = $"{client.EmailConfirmedRedirectURL}?userId={userId}";
                        return RedirectPermanent(redirectUrl);
                    }
                // case TokenType.CHANGE_EMAIL:
                //     {
                //         var newEmail = user.Claims.Find((c) => c.ClaimType == "new_email");
                //         if (newEmail == null) return BadRequest("Email change was not requested.");

                //         var result = await UserManager.ChangeEmailAsync(user, newEmail.ClaimValue.ToString(), code);
                //         if (result.Succeeded)
                //         {
                //             await UserManager.RemoveClaimAsync(user, newEmail.ToClaim());
                //             return Ok("Email changed.");
                //         }
                //         return BadRequest("Could not change email.");
                //     }
                case TokenType.RESET_PASSWORD:
                    {
                        if (!await UserManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "ResetPassword", code))
                            return BadRequest("Invalid token.");

                        var authorizationCode = await UserManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode");
                        var redirectUrl = $"{client.AccountRecoveryRedirectURL}?userId={userId}&code={authorizationCode}";
                        return RedirectPermanent(redirectUrl);
                    }
                default:
                    return BadRequest("Invalid type.");
            }

        }

        [HttpPost("verify")]
        public async Task<IActionResult> SendVerificationEmail()
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User);
            if (!await IsUserValidAsync(user, client.Id)) return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            var callbackUrl = Url.TokenLink(user.Id.ToString(), code, client.Id, TokenType.CONFRIM_EMAIL, Request.Scheme);
            await EmailSender.SendConfirmationEmailAsync(user.Email, callbackUrl, client);
            return Ok();
        }

        [HttpPost("unregister")]
        public async Task<IActionResult> UnregisterAccountAync([FromForm] DeleteAccountForm form)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User);
            if (!await IsUserValidAsync(user, client.Id)) return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            if (!await UserManager.CheckPasswordAsync(user, form.Password))
            {
                return Unauthorized();
            }

            await UserManager.RemoveFromRoleAsync(user, client.Id);
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetUserAccount()
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User);
            if (!await IsUserValidAsync(user, client.Id))
                return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            return Ok(new UserModel
            {
                UserId = user.Id.ToString(),
                Email = user.Email,
                IsEmailConfirmed = user.EmailConfirmed,
                // PhoneNumber = user.PhoneNumberConfirmed ? user.PhoneNumber : null,
                MFA = new MFAConfig
                {
                    IsEnabled = user.TwoFactorEnabled,
                    PrimaryMethod = MFAService.GetPrimaryMethod(user),
                    SecondaryMethod = MFAService.GetSecondaryMethod(user),
                    RemainingValidCodes = await MFAService.GetRemainingValidCodesAsync(user)
                }
            });
        }

        [HttpPost("recover")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetUserPassword([FromForm] ResetPasswordForm form)
        {
            var client = Clients.FindClientById(form.ClientId);
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.FindByEmailAsync(form.Email);
            if (!await IsUserValidAsync(user, form.ClientId)) return Ok();

            var code = await UserManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "ResetPassword");
            var callbackUrl = Url.TokenLink(user.Id.ToString(), code, client.Id, TokenType.RESET_PASSWORD, Request.Scheme);
#if DEBUG
            return Ok(callbackUrl);
#else
            await Slogger<AccountController>.Info("ResetUserPassword", user.Email, callbackUrl);
            await EmailSender.SendPasswordResetEmailAsync(user.Email, callbackUrl, client);
            return Ok();
#endif
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User);
            if (!await IsUserValidAsync(user, client.Id)) return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            var subjectId = User.FindFirstValue("sub");
            var jti = User.FindFirstValue("jti");

            var grants = await PersistedGrantStore.GetAllAsync(new PersistedGrantFilter
            {
                ClientId = client.Id,
                SubjectId = subjectId
            });
            grants = grants.Where((grant) => grant.Data.Contains(jti));
            if (grants.Any())
            {
                foreach (var grant in grants)
                {
                    await PersistedGrantStore.RemoveAsync(grant.Key);
                }
            }
            return Ok();
        }

        [HttpPost("token")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAccessTokenFromCode([FromForm] GetAccessTokenForm form)
        {
            if (!Clients.IsValidClient(form.ClientId)) return BadRequest("Invalid clientId.");
            var user = await UserManager.FindByIdAsync(form.UserId);
            if (!await IsUserValidAsync(user, form.ClientId))
                return BadRequest($"Unable to find user with ID '{form.UserId}'.");

            if (!await UserManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode", form.Code))
                return BadRequest("Invalid authorization_code.");
            var token = await TokenGenerationService.CreateAccessTokenAsync(user, form.ClientId);
            return Ok(new
            {
                access_token = token,
                expires_in = 18000
            });
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateAccount([FromForm] UpdateUserForm form)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User);
            if (!await IsUserValidAsync(user, client.Id))
                return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            switch (form.Type)
            {
                case "change_email":
                    {
                        var code = await UserManager.GenerateChangeEmailTokenAsync(user, form.NewEmail);
                        // var callbackUrl = Url.TokenLink(user.Id.ToString(), code, client.Id, TokenType.CHANGE_EMAIL, Request.Scheme);
                        await EmailSender.SendChangeEmailConfirmationAsync(user.Email, code, client);
                        await UserManager.AddClaimAsync(user, new Claim("new_email", form.NewEmail));
                        return Ok();
                    }
                case "change_password":
                    {
                        var result = await UserManager.ChangePasswordAsync(user, form.OldPassword, form.NewPassword);
                        if (result.Succeeded)
                        {
                            await SendPasswordChangedMessageAsync(user.Id.ToString());
                            return Ok();
                        }
                        return BadRequest(result.Errors.ToErrors());
                    }
                case "reset_password":
                    {
                        var result = await UserManager.RemovePasswordAsync(user);
                        if (result.Succeeded)
                        {
                            result = await UserManager.AddPasswordAsync(user, form.NewPassword);
                            if (result.Succeeded)
                            {
                                await SendPasswordChangedMessageAsync(user.Id.ToString());
                                return Ok();
                            }
                        }
                        return BadRequest(result.Errors.ToErrors());
                    }
            }
            return BadRequest("Invalid type.");
        }

        [HttpPost("sessions/clear")]
        public async Task<IActionResult> ClearUserSessions([FromQuery] bool all, [FromForm] string refresh_token)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User);
            if (!await IsUserValidAsync(user, client.Id)) return BadRequest($"Unable to find user with ID '{user.Id.ToString()}'.");

            var jti = User.FindFirstValue("jti");

            var grants = await PersistedGrantStore.GetAllAsync(new PersistedGrantFilter
            {
                ClientId = client.Id,
                SubjectId = user.Id.ToString()
            });
            foreach (var grant in grants)
            {
                if (!all && (grant.Data.Contains(jti) || grant.Data.Contains(refresh_token))) continue;
                await PersistedGrantStore.RemoveAsync(grant.Key);
            }
            return Ok();
        }

        private async Task SendPasswordChangedMessageAsync(string userId)
        {
            await WampServers.MessengerServer.PublishMessageAsync(WampServers.MessengerServer.Topics.SendSSETopic, new SendSSEMessage
            {
                UserId = userId,
                OriginTokenId = User.FindFirstValue("jti"),
                Message = new Message
                {
                    Type = "userPasswordChanged"
                }
            });
        }

        public async Task<bool> IsUserValidAsync(User user, string clientId)
        {
            return user != null && await UserManager.IsInRoleAsync(user, clientId);
        }
    }
}