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
using System.Threading.Tasks;
using IdentityModel;
using IdentityServer4.Models;
using IdentityServer4.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Ng.Services;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Services
{
    public class ProfileService : IProfileService
    {
        protected UserManager<User> UserManager { get; set; }
        private IHttpContextAccessor HttpContextAccessor { get; set; }

        public ProfileService(UserManager<User> userManager, IHttpContextAccessor httpContextAccessor)
        {
            UserManager = userManager;
            HttpContextAccessor = httpContextAccessor;
        }

        public async Task GetProfileDataAsync(ProfileDataRequestContext context)
        {
            User? user = await UserManager.GetUserAsync(context.Subject);
            if (user == null) return;

            IList<string> roles = await UserManager.GetRolesAsync(user);
            IList<Claim> claims = user.Claims.Select((c) => c.ToClaim()).ToList();

            context.IssuedClaims.AddRange(roles.Select((r) => new Claim(JwtClaimTypes.Role, r)));
            context.IssuedClaims.AddRange(claims);

            var httpContext = HttpContextAccessor.HttpContext;
            if (httpContext == null) return;

            var userAgentHeader = httpContext.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrEmpty(userAgentHeader)) return;

            var userAgentService = new UserAgentService();
            var ua = userAgentService.Parse(userAgentHeader);
            string? browser = null;
            if (userAgentHeader.Contains("Electron/", StringComparison.OrdinalIgnoreCase))
            {
                var electronMatch = System.Text.RegularExpressions.Regex.Match(
                    userAgentHeader,
                    @"Electron/([\d.]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                browser = electronMatch.Success
                    ? $"Electron {electronMatch.Groups[1].Value}"
                    : "Electron";
            }
            else if (!string.IsNullOrEmpty(ua.Browser))
            {
                browser = $"{ua.Browser} {ua.BrowserVersion}";
            }

            if (!string.IsNullOrEmpty(browser))
            {
                context.IssuedClaims.Add(new Claim("device_browser", browser));
            }
            if (!string.IsNullOrEmpty(ua.Platform))
            {
                context.IssuedClaims.Add(new Claim("device_platform", ua.Platform));
            }
        }

        public Task IsActiveAsync(IsActiveContext context)
        {
            return Task.CompletedTask;
        }
    }
}