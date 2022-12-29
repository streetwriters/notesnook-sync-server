/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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

using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Notesnook.API.Extensions
{
    public class AuthorizationResultTransformer : IAuthorizationMiddlewareResultHandler
    {
        private readonly IAuthorizationMiddlewareResultHandler _handler;

        public AuthorizationResultTransformer()
        {
            _handler = new AuthorizationMiddlewareResultHandler();
        }

        public async Task HandleAsync(
            RequestDelegate requestDelegate,
            HttpContext httpContext,
            AuthorizationPolicy authorizationPolicy,
            PolicyAuthorizationResult policyAuthorizationResult)
        {
            var isWebsocket = httpContext.Request.Headers.Upgrade == "websocket";

            if (!isWebsocket && policyAuthorizationResult.Forbidden && policyAuthorizationResult.AuthorizationFailure != null)
            {
                var error = string.Join("\n", policyAuthorizationResult.AuthorizationFailure.FailureReasons.Select((r) => r.Message));

                if (!string.IsNullOrEmpty(error) && !isWebsocket)
                {
                    httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    httpContext.Response.ContentType = "application/json";
                    await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new { error }));
                    return;
                }

                await _handler.HandleAsync(requestDelegate, httpContext, authorizationPolicy, policyAuthorizationResult);
            }
            else if (isWebsocket)
            {
                await _handler.HandleAsync(requestDelegate, httpContext, authorizationPolicy, PolicyAuthorizationResult.Success());
            }
            else
            {
                await _handler.HandleAsync(requestDelegate, httpContext, authorizationPolicy, policyAuthorizationResult);
            }
        }
    }
}