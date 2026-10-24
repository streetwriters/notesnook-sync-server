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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Exceptions;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Enums;
using Streetwriters.Identity.Extensions;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Controllers
{
    [ApiController]
    [DisplayName("Account")]
    [Route("account")]
    [Authorize(Config.ACCOUNT_API_POLICY)]
    public class AccountController : IdentityControllerBase
    {
        private static readonly string emailConfirmedPageHtml = HtmlHelper.ReadMinifiedHtmlFile("Templates/EmailConfirmedPage.html");
        private static readonly string emailConfirmErrorPageHtml = HtmlHelper.ReadMinifiedHtmlFile("Templates/EmailConfirmErrorPage.html");

        private ISessionStore SessionStore { get; set; }
        private ITokenIssuer TokenIssuer { get; set; }
        private IUserAccountService UserAccountService { get; set; }
        private EmailAddressValidator EmailValidator { get; set; }
        private readonly ILogger<AccountController> logger;

        public AccountController(
            UserManager<User> _userManager,
            ITemplatedEmailSender _emailSender,
            SignInManager<User> _signInManager,
            RoleManager<MongoRole> _roleManager,
            ISessionStore sessionStore,
            ITokenIssuer tokenIssuer,
            IMFAService _mfaService,
            IUserAccountService userAccountService,
            ILogger<AccountController> logger,
            EmailAddressValidator emailValidator
        ) : base(_userManager, _emailSender, _signInManager, _roleManager, _mfaService)
        {
            SessionStore = sessionStore;
            TokenIssuer = tokenIssuer;
            UserAccountService = userAccountService;
            EmailValidator = emailValidator;
            this.logger = logger;
        }

        [HttpGet("confirm")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> ConfirmToken(string userId, string code, string clientId, TokenType type)
        {
            var client = Clients.FindClientById(clientId);
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.FindByIdAsync(userId) ?? throw new NotFoundException("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, clientId)) return BadRequest($"Unable to find user with ID '{userId}'.");

            switch (type)
            {
                case TokenType.CONFRIM_EMAIL:
                    {
                        if (await UserManager.IsEmailConfirmedAsync(user))
                        {
                            return Content(
                                emailConfirmedPageHtml.Replace("{{subheading}}", "Your email is already verified."),
                                "text/html"
                            );
                        }

                        var result = await UserManager.ConfirmEmailAsync(user, code);
                        if (!result.Succeeded)
                        {
                            return Content(
                                emailConfirmErrorPageHtml.Replace("{{errors}}", string.Join(" ", result.Errors.ToErrors())),
                                "text/html"
                            );
                        }

                        if (await UserManager.IsInRoleAsync(user, client.Id) && client.OnEmailConfirmed != null)
                        {
                            logger.LogInformation("Email confirmed for user {UserId} on client {ClientId}. Triggering OnEmailConfirmed callback.", userId, client.Id);
                            await client.OnEmailConfirmed(userId);
                        }

                        return Content(
                            emailConfirmedPageHtml.Replace("{{subheading}}", "Your email has been confirmed."),
                            "text/html"
                        );
                    }
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
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> SendVerificationEmail([FromForm] string? newEmail)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User) ?? throw new NotFoundException("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, client.Id)) return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            if (string.IsNullOrEmpty(newEmail))
            {
                ArgumentNullException.ThrowIfNull(user.Email);
                var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
                var callbackUrl = UrlExtensions.TokenLink(user.Id.ToString(), code, client.Id, TokenType.CONFRIM_EMAIL);
                await EmailSender.SendConfirmationEmailAsync(user.Email, callbackUrl, client);
            }
            else
            {
                if (!await EmailValidator.IsEmailAddressValidAsync(newEmail.ToLowerInvariant()))
                {
                    return BadRequest("Invalid email address.");
                }

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
            var user = await UserManager.GetUserAsync(User) ?? throw new NotFoundException("User not found.");
            return Ok(await UserAccountService.GetUserAsync(client.Id, user.Id.ToString()));
        }

        [HttpPost("recover")]
        [AllowAnonymous]
        [EnableRateLimiting("strict")]
        public async Task<IActionResult> ResetUserPassword([FromForm] ResetPasswordForm form)
        {

            var client = Clients.FindClientById(form.ClientId);
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.FindByEmailAsync(form.Email);
            if (user == null || !await UserService.IsUserValidAsync(UserManager, user, form.ClientId)) return Ok();

            var code = await UserManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "ResetPassword");
            var callbackUrl = UrlExtensions.TokenLink(user.Id.ToString(), code, client.Id, TokenType.RESET_PASSWORD);
#if (DEBUG || STAGING)
            return Ok(callbackUrl);
#else
                        logger.LogInformation("Password reset email sent to: {Email}, callback URL: {CallbackUrl}", user.Email, callbackUrl);
                        await EmailSender.SendPasswordResetEmailAsync(user.Email, callbackUrl, client);
                        return Ok();
#endif
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User) ?? throw new NotFoundException("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, client.Id)) return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            var subjectId = User.FindFirstValue("sub");
            var jti = User.FindFirstValue("jti");

            if (subjectId != null && jti != null) await SessionStore.EndSessionAsync(client.Id, subjectId, jti);
            return Ok();
        }

        [HttpPost("token")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAccessTokenFromCode([FromForm] GetAccessTokenForm form)
        {
            if (!Clients.IsValidClient(form.ClientId)) return BadRequest("Invalid clientId.");
            var user = await UserManager.FindByIdAsync(form.UserId) ?? throw new NotFoundException("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, form.ClientId))
                return BadRequest($"Unable to find user with ID '{form.UserId}'.");

            if (!await UserManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode", form.Code))
                return BadRequest("Invalid authorization_code.");
            var token = await TokenIssuer.IssueAccessTokenAsync(user, form.ClientId, 1800, [Constants.PASSWORD_RESET_AUTHENTICATION_METHOD]);
            return Ok(new
            {
                access_token = token,
                scope = string.Join(' ', Config.ApiScopes.Select(s => s.Name)),
                expires_in = 1800
            });
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateAccount([FromForm] UpdateUserForm form)
        {
            var client = Clients.FindClientById(User.FindFirstValue("client_id"));
            if (client == null) return BadRequest("Invalid client_id.");

            var user = await UserManager.GetUserAsync(User) ?? throw new NotFoundException("User not found.");
            if (!await UserService.IsUserValidAsync(UserManager, user, client.Id))
                return BadRequest($"Unable to find user with ID '{UserManager.GetUserId(User)}'.");

            switch (form.Type)
            {
                case "change_email":
                    {
                        ArgumentNullException.ThrowIfNull(form.NewEmail);
                        ArgumentNullException.ThrowIfNull(form.Password);
                        ArgumentNullException.ThrowIfNull(form.VerificationCode);
                        foreach (var validator in UserManager.PasswordValidators)
                        {
                            var validation = await validator.ValidateAsync(UserManager, user, form.Password);
                            if (!validation.Succeeded) return BadRequest(validation.Errors.ToErrors());
                        }

                        // the password is derived from the email, so the new email, username and
                        // password must be saved together. ChangeEmailAsync persists all of them.
                        user.PasswordHash = UserManager.PasswordHasher.HashPassword(user, form.Password);
                        user.UserName = form.NewEmail;
                        await UserManager.UpdateNormalizedUserNameAsync(user);
                        var result = await UserManager.ChangeEmailAsync(user, form.NewEmail, form.VerificationCode);
                        if (!result.Succeeded) return BadRequest(result.Errors.ToErrors());

                        await SendLogoutMessageAsync(user.Id.ToString(), "Email changed.");
                        return Ok();
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
            var jti = User.FindFirstValue("jti");
            var userId = User.FindFirstValue("sub") ?? throw new InvalidOperationException("sub claim is missing");
            var clientId = User.FindFirstValue("client_id");
            if (await UserAccountService.ClearSessionsAsync(userId, clientId, all, jti, refresh_token))
                await SendLogoutMessageAsync(userId, "Session revoked.");
            return Ok();
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
