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
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IdentityServer4;
using IdentityServer4.AspNetIdentity;
using IdentityServer4.Models;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using static IdentityModel.OidcConstants;

namespace Streetwriters.Identity.Validation
{
    public class CustomResourceOwnerValidator : IResourceOwnerPasswordValidator
    {
        private UserManager<User> UserManager { get; set; }
        private SignInManager<User> SignInManager { get; set; }
        private IMFAService MFAService { get; set; }
        private ITokenGenerationService TokenGenerationService { get; set; }
        private IdentityServerTools Tools { get; set; }
        public CustomResourceOwnerValidator(UserManager<User> userManager, SignInManager<User> signInManager, IMFAService mfaService, ITokenGenerationService tokenGenerationService, IdentityServerTools tools)
        {
            UserManager = userManager;
            SignInManager = signInManager;
            MFAService = mfaService;
            TokenGenerationService = tokenGenerationService;
            Tools = tools;
        }

        public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
        {
            var user = await UserManager.FindByNameAsync(context.UserName);
            if (user != null)
            {
                var result = await SignInManager.CheckPasswordSignInAsync(user, context.Password, true);

                if (result.IsLockedOut)
                {
                    var timeLeft = user.LockoutEnd - DateTimeOffset.Now;
                    context.Result.IsError = true;
                    context.Result.Error = "user_locked_out";
                    context.Result.ErrorDescription = $"You have been locked out. Please try again in {Pluralize(timeLeft?.Minutes, "minute", "minutes")} and {Pluralize(timeLeft?.Seconds, "second", "seconds")}.";
                    return;
                }

                var success = result.Succeeded;
                var isMultiFactor = await UserManager.GetTwoFactorEnabledAsync(user);

                // We'll ask for 2FA regardless of password being incorrect to prevent an attacker
                // from knowing whether the password is correct or not.
                if (isMultiFactor)
                {
                    var primaryMethod = MFAService.GetPrimaryMethod(user);
                    var secondaryMethod = MFAService.GetSecondaryMethod(user);

                    var mfaCode = context.Request.Raw["mfa:code"];
                    var mfaMethod = context.Request.Raw["mfa:method"];

                    if (string.IsNullOrEmpty(mfaCode) || !MFAService.IsValidMFAMethod(mfaMethod))
                    {
                        var sendPhoneNumber = primaryMethod == MFAMethods.SMS || secondaryMethod == MFAMethods.SMS;

                        var token = await TokenGenerationService.CreateAccessTokenFromValidatedRequestAsync(context.Request, user, new[] { Config.MFA_GRANT_TYPE_SCOPE });
                        context.Result.CustomResponse = new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["error"] = "mfa_required",
                            ["error_description"] = "Multifactor authentication required.",
                            ["data"] = JsonSerializer.Serialize(new MFARequiredResponse
                            {
                                PhoneNumber = sendPhoneNumber ? Regex.Replace(user.PhoneNumber, @"\d(?!\d{0,3}$)", "*") : null,
                                PrimaryMethod = primaryMethod,
                                SecondaryMethod = secondaryMethod,
                                Token = token,
                            })
                        };
                        context.Result.IsError = true;
                        return;
                    }
                    else if (mfaMethod == MFAMethods.RecoveryCode)
                    {
                        var recoveryCodeResult = await UserManager.RedeemTwoFactorRecoveryCodeAsync(user, mfaCode);
                        if (!recoveryCodeResult.Succeeded)
                        {
                            context.Result.IsError = true;
                            context.Result.Error = "invalid_2fa_recovery_code";
                            context.Result.ErrorDescription = recoveryCodeResult.Errors.ToErrors().First();
                            return;
                        }
                    }
                    else
                    {
                        var provider = mfaMethod == MFAMethods.Email || mfaMethod == MFAMethods.SMS ? TokenOptions.DefaultPhoneProvider : UserManager.Options.Tokens.AuthenticatorTokenProvider;
                        var isMFACodeValid = await MFAService.VerifyOTPAsync(user, mfaCode, mfaMethod);
                        if (!isMFACodeValid)
                        {
                            context.Result.IsError = true;
                            context.Result.Error = "invalid_2fa_code";
                            context.Result.ErrorDescription = "Please provide a valid multi factor authentication code.";
                            return;
                        }
                    }

                    // if we are here, it means we succeeded.
                    success = true;
                }

                if (success)
                {
                    var sub = await UserManager.GetUserIdAsync(user);
                    context.Result = new GrantValidationResult(sub, AuthenticationMethods.Password);
                    return;
                }
            }

            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant);
        }

        string Pluralize(int? value, string singular, string plural)
        {
            if (value == null) return $"0 {plural}";
            return value == 1 ? $"{value} {singular}" : $"{value} {plural}";
        }
    }
}