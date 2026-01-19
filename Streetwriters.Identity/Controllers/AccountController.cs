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
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AspNetCore.Identity.Mongo.Model;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Enums;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using Streetwriters.Identity.Services;
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
        private IUserAccountService UserAccountService { get; set; }
        private readonly ILogger<AccountController> logger;
        public AccountController(UserManager<User> _userManager, ITemplatedEmailSender _emailSender,
        SignInManager<User> _signInManager, RoleManager<MongoRole> _roleManager, IPersistedGrantStore store,
        ITokenGenerationService tokenGenerationService, IMFAService _mfaService, IUserAccountService userAccountService, ILogger<AccountController> logger) : base(_userManager, _emailSender, _signInManager, _roleManager, _mfaService)
        {
            PersistedGrantStore = store;
            TokenGenerationService = tokenGenerationService;
            UserAccountService = userAccountService;
            this.logger = logger;
        }

        [HttpGet("confirm")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> ConfirmToken(string userId, string code, string clientId, TokenType type)
        {
            var client = Clients.FindClientById(clientId);
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, clientId)) return BadRequest($"Unable to find user with ID '{userId}'.");

            switch (type)
            {
                case TokenType.CONFRIM_EMAIL:
                    {
                        if (await UserManager.IsEmailConfirmedAsync(user)) return Ok("Email already verified.");

                        var result = await UserManager.ConfirmEmailAsync(user, code);
                        if (!result.Succeeded) return BadRequest(result.Errors.ToErrors());

                        if (await UserManager.IsInRoleAsync(user, client.Id) && client.OnEmailConfirmed != null)
                        {
                            await client.OnEmailConfirmed(userId);
                        }

                        if (!await UserManager.GetTwoFactorEnabledAsync(user))
                            await MFAService.EnableMFAAsync(user, MFAMethods.Email);

                        var redirectUrl = $"{client.EmailConfirmedRedirectURL}?userId={userId}";
                        return RedirectPermanent(redirectUrl);
                    }
                case TokenType.RESET_PASSWORD:
                    {
                        // if (!await UserManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "ResetPassword", code))
                        return BadRequest("Password reset is temporarily disabled due to some issues. It should be back soon. We apologize for the inconvenience.");

                        // var authorizationCode = await UserManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode");
                        // var redirectUrl = $"{client.AccountRecoveryRedirectURL}?userId={userId}&code={authorizationCode}";
                        // return RedirectPermanent(redirectUrl);
                    }
                default:
                    return BadRequest("Invalid type.");
            }

        }

        [HttpPost("verify")]
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> SendVerificationEmail([FromForm] string newEmail)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, client.Id)) return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            if (string.IsNullOrEmpty(newEmail))
            {
                ArgumentNullException.ThrowIfNull(user.Email);
                var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
                var callbackUrl = Url.TokenLink(user.Id.ToString(), code, client.Id, TokenType.CONFRIM_EMAIL);
                await EmailSender.SendConfirmationEmailAsync(user.Email, callbackUrl, client);
            }
            else
            {
                var code = await UserManager.GenerateChangeEmailTokenAsync(user, newEmail);
                await EmailSender.SendChangeEmailConfirmationAsync(newEmail, code, client);
            }
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetUserAccount()
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");
            var user = await UserManager.GetUserAsync(User) ?? throw new Exception("User not found.");
            return Ok(UserAccountService.GetUserAsync(client.Id, user.Id.ToString()));
        }

        [HttpPost("recover")]
        [AllowAnonymous]
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> ResetUserPassword([FromForm] ResetPasswordForm form)
        {
            return BadRequest(new { error = "Password reset is temporarily disabled due to some issues. It should be back soon. We apologize for the inconvenience." });
            //             var client = Clients.FindClientById(form.ClientId);
            //             if (client == null) return BadRequest("Invalid client_id.");

            //             var user = await UserManager.FindByEmailAsync(form.Email) ?? throw new Exception("User not found.");
            //             if (!await UserService.IsUserValidAsync(UserManager, user, form.ClientId)) return Ok();

            //             var code = await UserManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "ResetPassword");
            //             var callbackUrl = Url.TokenLink(user.Id.ToString(), code, client.Id, TokenType.RESET_PASSWORD);
            // #if (DEBUG || STAGING)
            //             return Ok(callbackUrl);
            // #else
            //             logger.LogInformation("Password reset email sent to: {Email}, callback URL: {CallbackUrl}", user.Email, callbackUrl);
            //             await EmailSender.SendPasswordResetEmailAsync(user.Email, callbackUrl, client);
            //             return Ok();
            // #endif
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, client.Id)) return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            var subjectId = User.FindFirstValue("sub");
            var jti = User.FindFirstValue("jti");

            var grants = await PersistedGrantStore.GetAllAsync(new PersistedGrantFilter
            {
                ClientId = client.Id,
                SubjectId = subjectId
            });
            grants = jti == null ? [] : grants.Where((grant) => grant.Data.Contains(jti));
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
            var user = await UserManager.FindByIdAsync(form.UserId) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, form.ClientId))
                return BadRequest($"Unable to find user with ID '{form.UserId}'.");

            if (!await UserManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode", form.Code))
                return BadRequest("Invalid authorization_code.");
            var token = await TokenGenerationService.CreateAccessTokenAsync(user, form.ClientId);
            return Ok(new
            {
                access_token = token,
                scope = string.Join(' ', Config.ApiScopes.Select(s => s.Name)),
                expires_in = 18000
            });
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateAccount([FromForm] UpdateUserForm form)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, client.Id))
                return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            switch (form.Type)
            {
                case "change_email":
                    {
                        ArgumentNullException.ThrowIfNull(form.NewEmail);
                        ArgumentNullException.ThrowIfNull(form.Password);
                        ArgumentNullException.ThrowIfNull(form.VerificationCode);
                        var result = await UserManager.ChangeEmailAsync(user, form.NewEmail, form.VerificationCode);
                        if (result.Succeeded)
                        {
                            result = await UserManager.RemovePasswordAsync(user);
                            if (result.Succeeded)
                            {
                                result = await UserManager.AddPasswordAsync(user, form.Password);
                                if (result.Succeeded)
                                {
                                    await UserManager.SetUserNameAsync(user, form.NewEmail);
                                    await SendLogoutMessageAsync(user.Id.ToString(), "Email changed.");
                                    return Ok();
                                }
                            }
                        }
                        return BadRequest(result.Errors.ToErrors());
                    }
                case "change_password":
                    {
                        return BadRequest(new { error = "Password change is temporarily disabled due to some issues. It should be back soon. We apologize for the inconvenience." });
                        // ArgumentNullException.ThrowIfNull(form.OldPassword);
                        // ArgumentNullException.ThrowIfNull(form.NewPassword);
                        // var result = await UserManager.ChangePasswordAsync(user, form.OldPassword, form.NewPassword);
                        // if (result.Succeeded)
                        // {
                        //     await SendLogoutMessageAsync(user.Id.ToString(), "Password changed.");
                        //     return Ok();
                        // }
                        // return BadRequest(result.Errors.ToErrors());
                    }
                case "reset_password":
                    {
                        return BadRequest(new { error = "Password reset is temporarily disabled due to some issues. It should be back soon. We apologize for the inconvenience." });
                        // ArgumentNullException.ThrowIfNull(form.NewPassword);
                        // var result = await UserManager.RemovePasswordAsync(user);
                        // if (result.Succeeded)
                        // {
                        //     await MFAService.ResetMFAAsync(user);
                        //     result = await UserManager.AddPasswordAsync(user, form.NewPassword);
                        //     if (result.Succeeded)
                        //     {
                        //         await SendLogoutMessageAsync(user.Id.ToString(), "Password reset.");
                        //         return Ok();
                        //     }
                        // }
                        // return BadRequest(result.Errors.ToErrors());
                    }
                case "change_marketing_consent":
                    {
                        var claimType = $"{client.Id}:marketing_consent";
                        var claims = await UserManager.GetClaimsAsync(user);
                        var marketingConsentClaim = claims.FirstOrDefault((claim) => claim.Type == claimType);
                        if (marketingConsentClaim != null) await UserManager.RemoveClaimAsync(user, marketingConsentClaim);
                        if (!form.Enabled)
                            await UserManager.AddClaimAsync(user, new Claim(claimType, "false"));
                        return Ok();
                    }

            }
            return BadRequest("Invalid type.");
        }

        [HttpPost("sessions/clear")]
        public async Task<IActionResult> ClearUserSessions([FromQuery] bool all, [FromForm] string? refresh_token)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, client.Id)) return BadRequest($"Unable to find user with ID '{user.Id}'.");

            var jti = User.FindFirstValue("jti");

            var grants = await PersistedGrantStore.GetAllAsync(new PersistedGrantFilter
            {
                ClientId = client.Id,
                SubjectId = user.Id.ToString()
            });
            string? refreshTokenKey = refresh_token != null ? GetHashedKey(refresh_token, PersistedGrantTypes.RefreshToken) : null;
            var removedKeys = new List<string>();
            foreach (var grant in grants)
            {
                if (!all && (grant.Data.Contains(jti) || grant.Key == refreshTokenKey)) continue;
                await PersistedGrantStore.RemoveAsync(grant.Key);
                removedKeys.Add(grant.Key);
            }

            await WampServers.NotesnookServer.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(removedKeys));
            await WampServers.MessengerServer.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(removedKeys));
            await WampServers.SubscriptionServer.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(removedKeys));
            await SendLogoutMessageAsync(user.Id.ToString(), "Session revoked.");
            return Ok();
        }

        private static string GetHashedKey(string value, string grantType)
        {
            return (value + ":" + grantType).Sha256();
        }

        private async Task SendLogoutMessageAsync(string userId, string reason)
        {
            await SendMessageAsync(userId, new Message
            {
                Type = "logout",
                Data = JsonSerializer.Serialize(new { reason })
            });
        }

        private async Task SendMessageAsync(string userId, Message message)
        {
            await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
            {
                UserId = userId,
                OriginTokenId = User.FindFirstValue("jti"),
                Message = message
            });
        }
    }
}
