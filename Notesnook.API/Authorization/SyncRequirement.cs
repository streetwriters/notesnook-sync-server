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
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Notesnook.API.Authorization
{
    public class SyncRequirement : AuthorizationHandler<SyncRequirement>, IAuthorizationRequirement
    {
        private readonly Dictionary<string, string> pathErrorPhraseMap = new()
        {
            ["/sync/attachments"] = "use attachments",
            ["/sync"] = "sync your notes",
            ["/hubs/sync"] = "sync your notes",
            ["/hubs/sync/v2"] = "sync your notes",
            ["/monographs"] = "publish monographs"
        };

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SyncRequirement requirement)
        {
            PathString path = context.Resource is DefaultHttpContext httpContext ? httpContext.Request.Path : null;
            var result = this.IsAuthorized(context.User, path);
            if (result.Succeeded) context.Succeed(requirement);
            else if (result.AuthorizationFailure.FailureReasons.Any())
                context.Fail(result.AuthorizationFailure.FailureReasons.First());
            else context.Fail();

            return Task.CompletedTask;
        }

        public PolicyAuthorizationResult IsAuthorized(ClaimsPrincipal? User, PathString requestPath)
        {
            var id = User?.FindFirstValue("sub");

            if (string.IsNullOrEmpty(id))
            {
                var reason = new[]
                {
                    new AuthorizationFailureReason(this, "Invalid token.")
                };
                return PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed(reason));
            }

            var hasSyncScope = User.HasClaim("scope", "notesnook.sync");
            var isInAudience = User.HasClaim("aud", "notesnook");
            var hasRole = User.HasClaim("role", "notesnook");

            var isEmailVerified = User.HasClaim("verified", "true");

            if (!isEmailVerified)
            {
                var phrase = "continue";

                foreach (var item in pathErrorPhraseMap)
                {
                    if (requestPath != null && requestPath.StartsWithSegments(item.Key))
                        phrase = item.Value;
                }

                var error = $"Please confirm your email to {phrase}.";
                var reason = new[]
                {
                    new AuthorizationFailureReason(this, error)
                };
                return PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed(reason));
                //  context.Fail(new AuthorizationFailureReason(this, error));
            }

            if (hasSyncScope && isInAudience && hasRole && isEmailVerified)
                return PolicyAuthorizationResult.Success(); //(requirement);
            return PolicyAuthorizationResult.Forbid();
        }

        public override Task HandleAsync(AuthorizationHandlerContext context)
        {
            return this.HandleRequirementAsync(context, this);
        }

    }
}