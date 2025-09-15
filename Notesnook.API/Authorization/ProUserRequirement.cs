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

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Notesnook.API.Authorization
{
    public class ProUserRequirement : AuthorizationHandler<ProUserRequirement>, IAuthorizationRequirement
    {
        private readonly Dictionary<string, string> pathErrorPhraseMap = new()
        {
            ["/s3"] = "upload attachments",
            ["/s3/multipart"] = "upload attachments",
        };
        private static readonly string[] proClaims = ["premium", "premium_canceled"];
        private static readonly string[] trialClaims = ["trial"];
        public static bool IsUserPro(ClaimsPrincipal user)
        => user.Claims.Any((c) => c.Type == "notesnook:status" && proClaims.Contains(c.Value));
        public static bool IsUserTrialing(ClaimsPrincipal user)
        => user.Claims.Any((c) => c.Type == "notesnook:status" && trialClaims.Contains(c.Value));

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ProUserRequirement requirement)
        {
            PathString path = context.Resource is DefaultHttpContext httpContext ? httpContext.Request.Path : null;
            var isProOrTrial = IsUserPro(context.User) || IsUserTrialing(context.User);
            if (isProOrTrial) context.Succeed(requirement);
            else
            {
                var phrase = "continue";
                foreach (var item in pathErrorPhraseMap)
                {
                    if (path != null && path.StartsWithSegments(item.Key))
                        phrase = item.Value;
                }
                var error = $"Please upgrade to Pro to {phrase}.";
                context.Fail(new AuthorizationFailureReason(this, error));
            }
            return Task.CompletedTask;
        }

        public override Task HandleAsync(AuthorizationHandlerContext context)
        {
            return this.HandleRequirementAsync(context, this);
        }
    }
}