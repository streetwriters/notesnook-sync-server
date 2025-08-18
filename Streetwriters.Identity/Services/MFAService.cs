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
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Services
{
    internal class MFAService : IMFAService
    {
        const string PRIMARY_METHOD_CLAIM = "mfa:primary";
        const string SECONDARY_METHOD_CLAIM = "mfa:secondary";
        const string SMS_ID_CLAIM = "mfa:sms:id";

        private UserManager<User> UserManager { get; set; }
        private ITemplatedEmailSender EmailSender { get; set; }
        private ISMSSender SMSSender { get; set; }
        public MFAService(UserManager<User> _userManager, ITemplatedEmailSender emailSender, ISMSSender smsSender)
        {
            UserManager = _userManager;
            EmailSender = emailSender;
            SMSSender = smsSender;
        }

        public async Task EnableMFAAsync(User user, string primaryMethod)
        {
            var result = await UserManager.SetTwoFactorEnabledAsync(user, true);
            if (!result.Succeeded) return;

            await this.RemovePrimaryMethodAsync(user);
            await this.RemoveSecondaryMethodAsync(user);
            await UserManager.AddClaimAsync(user, new Claim(MFAService.PRIMARY_METHOD_CLAIM, primaryMethod));
        }

        public async Task<bool> DisableMFAAsync(User user)
        {
            var result = await UserManager.SetTwoFactorEnabledAsync(user, false);
            if (!result.Succeeded) return false;

            await this.RemovePrimaryMethodAsync(user);
            await this.RemoveSecondaryMethodAsync(user);

            await UserManager.ResetAuthenticatorKeyAsync(user);
            return true;
        }

        public async Task<bool> ResetMFAAsync(User user)
        {
            await UserManager.SetTwoFactorEnabledAsync(user, false);
            await UserManager.SetTwoFactorEnabledAsync(user, true);

            await this.RemovePrimaryMethodAsync(user);
            await this.RemoveSecondaryMethodAsync(user);

            await UserManager.AddClaimAsync(user, new Claim(MFAService.PRIMARY_METHOD_CLAIM, MFAMethods.Email));

            await UserManager.ResetAuthenticatorKeyAsync(user);
            return true;
        }

        public async Task SetSecondaryMethodAsync(User user, string secondaryMethod)
        {
            await this.ReplaceClaimAsync(user, MFAService.SECONDARY_METHOD_CLAIM, secondaryMethod);
        }

        private async Task ReplaceClaimAsync(User user, string claimType, string claimValue)
        {
            await this.RemoveClaimAsync(user, claimType);
            await UserManager.AddClaimAsync(user, new Claim(claimType, claimValue));
        }

        public string GetPrimaryMethod(User user)
        {
            return this.GetClaimValue(user, MFAService.PRIMARY_METHOD_CLAIM, MFAMethods.Email);
        }

        public string GetSecondaryMethod(User user)
        {
            return this.GetClaimValue(user, MFAService.SECONDARY_METHOD_CLAIM);
        }

        public string GetClaimValue(User user, string claimType, string defaultValue = null)
        {
            var claim = user.Claims.FirstOrDefault((c) => c.ClaimType == claimType);
            return claim != null ? claim.ClaimValue : defaultValue;
        }

        public Task<int> GetRemainingValidCodesAsync(User user)
        {
            return UserManager.CountRecoveryCodesAsync(user);
        }

        public bool IsValidMFAMethod(string method)
        {
            return method == MFAMethods.App || method == MFAMethods.Email || method == MFAMethods.SMS || method == MFAMethods.RecoveryCode;
        }

        public bool IsValidMFAMethod(string method, User user)
        {
            var primaryMethod = GetPrimaryMethod(user);
            var secondaryMethod = GetSecondaryMethod(user);
            if (!IsValidMFAMethod(method)) return false;
            return method == primaryMethod || (!string.IsNullOrEmpty(secondaryMethod) && method == secondaryMethod);
        }

        private Task RemoveSecondaryMethodAsync(User user)
        {
            return this.RemoveClaimAsync(user, MFAService.SECONDARY_METHOD_CLAIM);
        }

        private Task RemovePrimaryMethodAsync(User user)
        {
            return this.RemoveClaimAsync(user, MFAService.PRIMARY_METHOD_CLAIM);
        }

        private async Task RemoveClaimAsync(User user, string claimType)
        {
            var claim = user.Claims.FirstOrDefault((c) => c.ClaimType == claimType);
            if (claim != null) await UserManager.RemoveClaimAsync(user, claim.ToClaim());
        }

        public async Task<AuthenticatorDetails> GetAuthenticatorDetailsAsync(User user, IClient client)
        {
            // Load the authenticator key & QR code URI to display on the form
            var unformattedKey = await UserManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                await UserManager.ResetAuthenticatorKeyAsync(user);
                unformattedKey = await UserManager.GetAuthenticatorKeyAsync(user);
            }

            return new AuthenticatorDetails
            {
                SharedKey = FormatKey(unformattedKey),
                AuthenticatorUri = GenerateQrCodeUri(user.Email, unformattedKey, client.Name)
            };
        }

        public async Task SendOTPAsync(User user, IClient client, MultiFactorSetupForm form, bool isSetup = false)
        {
            var method = form.Type;
            if ((method != MFAMethods.Email && method != MFAMethods.SMS) || !IsValidMFAMethod(method))
                throw new Exception("Invalid method.");

            if (isSetup &&
                method == MFAMethods.SMS &&
                !UserService.IsUserPremium(client.Id, user))
                throw new Exception("Due to the high costs of SMS, currently 2FA via SMS is only available for Pro users.");

            // if (!user.EmailConfirmed) throw new Exception("Please confirm your email before activating 2FA by email.");
            await GetAuthenticatorDetailsAsync(user, client);

            switch (method)
            {
                case "email":
                    string emailOTP = await UserManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
                    await EmailSender.Send2FACodeEmailAsync(user.Email, emailOTP, client);
                    break;
                case "sms":
                    await UserManager.SetPhoneNumberAsync(user, form.PhoneNumber);
                    var id = await SMSSender.SendOTPAsync(form.PhoneNumber, client);
                    await this.ReplaceClaimAsync(user, MFAService.SMS_ID_CLAIM, id);
                    break;

            }
        }

        public async Task<bool> VerifyOTPAsync(User user, string code, string method)
        {
            if (method == MFAMethods.SMS)
            {
                var id = this.GetClaimValue(user, MFAService.SMS_ID_CLAIM);
                if (string.IsNullOrEmpty(id)) throw new Exception("Could not find associated SMS verify id. Please try sending the code again.");
                if (await SMSSender.VerifyOTPAsync(id, code))
                {
                    // Auto confirm user phone number if not confirmed
                    if (!await UserManager.IsPhoneNumberConfirmedAsync(user))
                    {
                        var token = await UserManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber);
                        await UserManager.VerifyChangePhoneNumberTokenAsync(user, token, user.PhoneNumber);
                    }
                    await this.RemoveClaimAsync(user, MFAService.SMS_ID_CLAIM);
                    return true;
                }
                return false;
            }
            else if (method == MFAMethods.Email)
            {
                if (await UserManager.VerifyTwoFactorTokenAsync(user, GetProvider(method), code))
                {
                    // Auto confirm user email if not confirmed
                    if (!await UserManager.IsEmailConfirmedAsync(user))
                    {
                        var token = await UserManager.GenerateEmailConfirmationTokenAsync(user);
                        await UserManager.ConfirmEmailAsync(user, token);
                    }
                    return true;
                }
                return false;
            }
            else
                return await UserManager.VerifyTwoFactorTokenAsync(user, GetProvider(method), code);
        }

        private string GetProvider(string method)
        {
            return method == MFAMethods.Email || method == MFAMethods.SMS ? TokenOptions.DefaultPhoneProvider : UserManager.Options.Tokens.AuthenticatorTokenProvider;
        }

        private string FormatKey(string unformattedKey)
        {
            var result = new StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition, 4)).Append(" ");
                currentPosition += 4;
            }
            if (currentPosition < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition));
            }

            return result.ToString().ToLowerInvariant();
        }

        private string GenerateQrCodeUri(string email, string unformattedKey, string issuer)
        {
            const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

            return string.Format(
                AuthenticatorUriFormat,
                UrlEncoder.Default.Encode(issuer),
                UrlEncoder.Default.Encode(email),
                unformattedKey);
        }
    }
}