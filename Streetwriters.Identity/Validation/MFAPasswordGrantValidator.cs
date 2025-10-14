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
using System.Threading.Tasks;
using IdentityServer4.Models;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using static IdentityModel.OidcConstants;

namespace Streetwriters.Identity.Validation
{
    public class MFAPasswordGrantValidator : IExtensionGrantValidator
    {
        private UserManager<User> UserManager { get; set; }
        private SignInManager<User> SignInManager { get; set; }
        private IMFAService MFAService { get; set; }
        private IHttpContextAccessor HttpContextAccessor { get; set; }
        private ITokenValidator TokenValidator { get; set; }
        private ITemplatedEmailSender EmailSender { get; set; }

        public MFAPasswordGrantValidator(UserManager<User> userManager, SignInManager<User> signInManager, IMFAService mfaService, IHttpContextAccessor httpContextAccessor, ITokenValidator tokenValidator, ITemplatedEmailSender emailSender)
        {
            UserManager = userManager;
            SignInManager = signInManager;
            MFAService = mfaService;
            HttpContextAccessor = httpContextAccessor;
            TokenValidator = tokenValidator;
            EmailSender = emailSender;
        }
        public string GrantType => Config.MFA_PASSWORD_GRANT_TYPE;

        public async Task ValidateAsync(ExtensionGrantValidationContext context)
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant);

            var httpContext = HttpContextAccessor.HttpContext;
            if (httpContext == null) return;

            var tokenResult = BearerTokenValidator.ValidateAuthorizationHeader(httpContext);
            if (!tokenResult.TokenFound) return;

            var tokenValidationResult = await TokenValidator.ValidateAccessTokenAsync(tokenResult.Token, Config.MFA_PASSWORD_GRANT_TYPE_SCOPE);
            if (tokenValidationResult.IsError) return;

            var client = Clients.FindClientById(context.Request.ClientId);
            if (client == null || context.Request.ClientId != tokenValidationResult.Claims.GetClaimValue("client_id"))
            {
                context.Result = new GrantValidationResult(TokenRequestErrors.InvalidClient);
                return;
            }

            var userId = tokenValidationResult.Claims.GetClaimValue("sub");
            var password = context.Request.Raw["password"];

            if (string.IsNullOrEmpty(userId)) return;

            var user = await UserManager.FindByIdAsync(userId);
            if (user == null) return;

            context.Result.Error = "unauthorized";
            context.Result.ErrorDescription = "Password is incorrect.";

            if (string.IsNullOrEmpty(password)) return;

            var result = await SignInManager.CheckPasswordSignInAsync(user, password, true);
            if (result.IsLockedOut)
            {
                var timeLeft = user.LockoutEnd - DateTimeOffset.Now;
                context.Result = new LockedOutValidationResult(timeLeft);
                return;
            }

            if (!result.Succeeded)
            {
                await EmailSender.SendFailedLoginAlertAsync(user.Email, httpContext.GetClientInfo(), client).ConfigureAwait(false);
                return;
            }

            await UserManager.ResetAccessFailedCountAsync(user);
            var sub = await UserManager.GetUserIdAsync(user);
            context.Result = new GrantValidationResult(sub, AuthenticationMethods.Password);
        }
    }
}