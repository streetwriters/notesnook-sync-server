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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Extensions;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Services
{
    /// <summary>
    /// How users sign in, through the password grant or the email → mfa → mfa_password grants.
    /// The token server only checks the step tokens and turns the results into tokens.
    /// </summary>
    public class GrantService(UserManager<User> userManager, SignInManager<User> signInManager, IMFAService mfaService, ITemplatedEmailSender emailSender)
    {
        public const string PasswordAuthenticationMethod = "pwd";

        public async Task<GrantResult> PasswordAsync(string userName, string password, string? mfaCode, string? mfaMethod)
        {
            var user = await userManager.FindByNameAsync(userName);
            if (user == null) return GrantResult.InvalidGrant;

            var result = await signInManager.CheckPasswordSignInAsync(user, password, true);
            if (result.IsLockedOut) return new GrantResult.Failed("user_locked_out", LockedOutMessage(user));

            // We'll ask for 2FA regardless of password being incorrect to prevent an attacker
            // from knowing whether the password is correct or not.
            if (await userManager.GetTwoFactorEnabledAsync(user))
            {
                if (string.IsNullOrEmpty(mfaCode) || string.IsNullOrEmpty(mfaMethod) || !mfaService.IsValidMFAMethod(mfaMethod, user))
                    return new GrantResult.MFARequired(user, MFARequiredResponseFor(user));

                if (mfaMethod == MFAMethods.RecoveryCode)
                {
                    var recoveryCodeResult = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, mfaCode);
                    if (!recoveryCodeResult.Succeeded)
                    {
                        await userManager.AccessFailedAsync(user);
                        return new GrantResult.Failed("invalid_2fa_recovery_code", recoveryCodeResult.Errors.ToErrors().First());
                    }
                }
                else if (!await mfaService.VerifyOTPAsync(user, mfaCode, mfaMethod))
                {
                    await userManager.AccessFailedAsync(user);
                    return new GrantResult.Failed("invalid_2fa_code", "Please provide a valid multi factor authentication code.");
                }
            }

            if (!result.Succeeded) return GrantResult.InvalidGrant;

            // CheckPasswordSignInAsync doesn't reset lockout when 2FA is enabled
            await userManager.ResetAccessFailedCountAsync(user);
            return new GrantResult.Authenticated(user, PasswordAuthenticationMethod);
        }

        public async Task<GrantResult> EmailAsync(string? email, string clientId, bool supportsOptionalMFA)
        {
            var registeredUser = await userManager.FindRegisteredUserAsync(email, clientId);
            var user = registeredUser ?? new User
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId(),
                Email = email,
                UserName = email,
                NormalizedEmail = email,
                NormalizedUserName = email,
                EmailConfirmed = false,
                SecurityStamp = ""
            };

            // Old clients always expect the MFA step so only skip it for clients that support optional 2FA.
            // Unregistered users must always go through the MFA step so we don't expose whether an account exists.
            if (registeredUser != null && !await userManager.GetTwoFactorEnabledAsync(user) && supportsOptionalMFA)
                return new GrantResult.Step(user, Config.MFA_PASSWORD_GRANT_TYPE_SCOPE, null);

            return new GrantResult.Step(user, Config.MFA_GRANT_TYPE_SCOPE, MFARequiredResponseFor(user));
        }

        /// <param name="userId">The subject of a valid token from the email step.</param>
        public async Task<GrantResult> MFAAsync(string userId, IClient client, string? mfaCode, string? mfaMethod, bool supportsOptionalMFA, HttpContext httpContext)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null) return UnregisteredMFA(mfaCode, mfaMethod);

            if (await userManager.IsLockedOutAsync(user))
                return new GrantResult.Failed("locked_out", LockedOutMessage(user));

            if (string.IsNullOrEmpty(mfaCode)) return InvalidMFACode;

            if (string.IsNullOrEmpty(mfaMethod) || (!mfaService.IsValidMFAMethod(mfaMethod, user) && mfaMethod != MFAMethods.RecoveryCode))
                return InvalidMFAMethod;

            if (mfaMethod == MFAMethods.RecoveryCode)
            {
                var result = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, mfaCode);
                if (!result.Succeeded)
                {
                    await userManager.AccessFailedAsync(user);
                    await emailSender.SendFailedLoginAlertAsync(user.Email!, httpContext.GetClientInfo(), client).ConfigureAwait(false);
                    return InvalidRecoveryCode;
                }
            }
            else if (!await mfaService.VerifyOTPAsync(user, mfaCode, mfaMethod))
            {
                await userManager.AccessFailedAsync(user);
                await emailSender.SendFailedLoginAlertAsync(user.Email!, httpContext.GetClientInfo(), client).ConfigureAwait(false);
                return InvalidMFACode;
            }

            // This must be done after the MFA code is verified
            // otherwise the security stamp will be updated
            // making all the 2FA code invalid.
            // Old clients don't support optional 2FA so we keep enforcing it for them.
            if (!supportsOptionalMFA && !await userManager.GetTwoFactorEnabledAsync(user))
                await mfaService.EnableMFAAsync(user, MFAMethods.Email);

            await userManager.ResetAccessFailedCountAsync(user);
            return new GrantResult.Step(user, Config.MFA_PASSWORD_GRANT_TYPE_SCOPE, null);
        }

        private static readonly GrantResult.Failed InvalidMFACode = new("invalid_mfa", "Please provide a valid multi-factor authentication code.");
        private static readonly GrantResult.Failed InvalidMFAMethod = new("invalid_mfa", "Please provide a valid multi-factor authentication method.");
        private static readonly GrantResult.Failed InvalidRecoveryCode = new("invalid_mfa", "Please provide a valid multi-factor authentication recovery code.");

        /// <summary>
        /// Answers as for a registered user, with email as their MFA method, whose code is wrong, so
        /// the MFA step doesn't reveal whether an account exists either.
        /// </summary>
        private GrantResult UnregisteredMFA(string? mfaCode, string? mfaMethod)
        {
            if (string.IsNullOrEmpty(mfaCode)) return InvalidMFACode;
            if (string.IsNullOrEmpty(mfaMethod) || (!mfaService.IsValidMFAMethod(mfaMethod, new User()) && mfaMethod != MFAMethods.RecoveryCode))
                return InvalidMFAMethod;
            return mfaMethod == MFAMethods.RecoveryCode ? InvalidRecoveryCode : InvalidMFACode;
        }

        /// <param name="userId">The subject of a valid token from the mfa step (or the email step, if it was skipped).</param>
        public async Task<GrantResult> MFAPasswordAsync(string userId, IClient client, string? password, HttpContext httpContext)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null) return GrantResult.InvalidGrant;

            var incorrect = new GrantResult.Failed("unauthorized", "Password is incorrect.");
            if (string.IsNullOrEmpty(password)) return incorrect;

            var result = await signInManager.CheckPasswordSignInAsync(user, password, true);
            if (result.IsLockedOut)
                return new GrantResult.Failed("locked_out", LockedOutMessage(user));

            if (!result.Succeeded)
            {
                await emailSender.SendFailedLoginAlertAsync(user.Email!, httpContext.GetClientInfo(), client).ConfigureAwait(false);
                return incorrect;
            }

            await userManager.ResetAccessFailedCountAsync(user);
            return new GrantResult.Authenticated(user, PasswordAuthenticationMethod);
        }

        private MFARequiredResponse MFARequiredResponseFor(User user)
        {
            var primaryMethod = mfaService.GetPrimaryMethod(user);
            var secondaryMethod = mfaService.GetSecondaryMethod(user);
            var sendPhoneNumber = primaryMethod == MFAMethods.SMS || secondaryMethod == MFAMethods.SMS;
            return new MFARequiredResponse
            {
                PhoneNumber = sendPhoneNumber && user.PhoneNumber != null ? Regex.Replace(user.PhoneNumber, @"\d(?!\d{0,3}$)", "*") : null,
                PrimaryMethod = primaryMethod,
                SecondaryMethod = secondaryMethod,
            };
        }

        public static string LockedOutMessage(User user)
        {
            // a later lockout, e.g. for failed attempts, ends at a different time
            if (user.ResetLockoutEnd != null && user.ResetLockoutEnd == user.LockoutEnd?.ToUnixTimeMilliseconds())
                return "Your account is being reset. Please try again in a minute.";
            var timeLeft = user.LockoutEnd - DateTimeOffset.Now;
            return timeLeft.HasValue
                ? $"You have been locked out. Please try again in {timeLeft.Value.Minutes.Pluralize("minute", "minutes")} and {timeLeft.Value.Seconds.Pluralize("second", "seconds")}."
                : "You have been locked out.";
        }
    }
}
