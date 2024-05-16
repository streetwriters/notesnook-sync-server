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
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IdentityModel;
using IdentityServer4.Models;
using IdentityServer4.Stores;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Ng.Services;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using static IdentityModel.OidcConstants;

namespace Streetwriters.Identity.Validation
{
    public class MFAGrantValidator : IExtensionGrantValidator
    {
        private UserManager<User> UserManager { get; set; }
        private SignInManager<User> SignInManager { get; set; }
        private IMFAService MFAService { get; set; }
        private IHttpContextAccessor HttpContextAccessor { get; set; }
        private ITokenValidator TokenValidator { get; set; }
        private ITokenGenerationService TokenGenerationService { get; set; }
        private IEmailSender EmailSender { get; set; }
        public MFAGrantValidator(UserManager<User> userManager, SignInManager<User> signInManager, IMFAService mfaService, IHttpContextAccessor httpContextAccessor, ITokenValidator tokenValidator, ITokenGenerationService tokenGenerationService, IEmailSender emailSender)
        {
            UserManager = userManager;
            SignInManager = signInManager;
            MFAService = mfaService;
            HttpContextAccessor = httpContextAccessor;
            TokenValidator = tokenValidator;
            TokenGenerationService = tokenGenerationService;
            EmailSender = emailSender;
        }

        public string GrantType => Config.MFA_GRANT_TYPE;

        public async Task ValidateAsync(ExtensionGrantValidationContext context)
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant);

            var httpContext = HttpContextAccessor.HttpContext;
            var tokenResult = BearerTokenValidator.ValidateAuthorizationHeader(httpContext);
            if (!tokenResult.TokenFound) return;

            var tokenValidationResult = await TokenValidator.ValidateAccessTokenAsync(tokenResult.Token, Config.MFA_GRANT_TYPE_SCOPE);
            if (tokenValidationResult.IsError) return;

            var client = Clients.FindClientById(tokenValidationResult.Claims.GetClaimValue("client_id"));
            if (client == null)
            {
                context.Result = new GrantValidationResult(TokenRequestErrors.InvalidClient);
                return;
            }

            var userId = tokenValidationResult.Claims.GetClaimValue("sub");
            var mfaCode = context.Request.Raw["mfa:code"];
            var mfaMethod = context.Request.Raw["mfa:method"];

            if (string.IsNullOrEmpty(userId)) return;

            var user = await UserManager.FindByIdAsync(userId);
            if (user == null) return;

            var isLockedOut = await UserManager.IsLockedOutAsync(user);
            if (isLockedOut)
            {
                var timeLeft = user.LockoutEnd - DateTimeOffset.Now;
                context.Result = new LockedOutValidationResult(timeLeft);
                return;
            }

            context.Result.Error = "invalid_mfa";
            context.Result.ErrorDescription = "Please provide a valid multi-factor authentication code.";

            if (!await UserManager.GetTwoFactorEnabledAsync(user))
                await MFAService.EnableMFAAsync(user, MFAMethods.Email);

            if (string.IsNullOrEmpty(mfaCode)) return;
            if (string.IsNullOrEmpty(mfaMethod) || !MFAService.IsValidMFAMethod(mfaMethod))
            {
                context.Result.ErrorDescription = "Please provide a valid multi-factor authentication method.";
                return;
            }

            if (mfaMethod == MFAMethods.RecoveryCode)
            {
                context.Result.ErrorDescription = "Please provide a valid multi-factor authentication recovery code.";

                var result = await UserManager.RedeemTwoFactorRecoveryCodeAsync(user, mfaCode);
                if (!result.Succeeded)
                {
                    await UserManager.AccessFailedAsync(user);
                    await EmailSender.SendFailedLoginAlertAsync(user.Email, httpContext.GetClientInfo(), client).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                if (!await MFAService.VerifyOTPAsync(user, mfaCode, mfaMethod))
                {
                    await UserManager.AccessFailedAsync(user);
                    await EmailSender.SendFailedLoginAlertAsync(user.Email, httpContext.GetClientInfo(), client).ConfigureAwait(false);
                    return;
                }
            }

            await UserManager.ResetAccessFailedCountAsync(user);
            context.Result.IsError = false;
            context.Result.Subject = await TokenGenerationService.TransformTokenRequestAsync(context.Request, user, GrantType, [Config.MFA_PASSWORD_GRANT_TYPE_SCOPE]);
        }


        string Pluralize(int? value, string singular, string plural)
        {
            if (value == null) return $"0 {plural}";
            return value == 1 ? $"{value} {singular}" : $"{value} {plural}";
        }
    }
}