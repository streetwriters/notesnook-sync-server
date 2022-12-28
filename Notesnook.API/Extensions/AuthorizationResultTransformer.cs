using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notesnook.API.Authorization;

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