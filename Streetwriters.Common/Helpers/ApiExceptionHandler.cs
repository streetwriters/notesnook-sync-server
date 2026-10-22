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
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Streetwriters.Common.Exceptions;
using WampSharp.V2.Core.Contracts;

namespace Streetwriters.Common.Helpers
{
    public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
    {
        private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(30);

        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (context.RequestAborted.IsCancellationRequested) return true;

            int statusCode;
            string error;
            TimeSpan? retryAfter = null;
            string? traceId = null;

            if (exception is ClientException clientException)
            {
                statusCode = clientException.StatusCode;
                error = clientException.Message;
                if (clientException is ServiceUnavailableException unavailable)
                {
                    retryAfter = unavailable.RetryAfter;
                    logger.LogWarning(exception, "Service unavailable: {Path}", context.Request.Path);
                }
            }
            else if (IsTransient(exception))
            {
                statusCode = StatusCodes.Status503ServiceUnavailable;
                error = "Service is temporarily unavailable. Please try again later.";
                retryAfter = DefaultRetryAfter;
                logger.LogWarning(exception, "Service unavailable: {Path}", context.Request.Path);
            }
            else
            {
                statusCode = StatusCodes.Status500InternalServerError;
                error = "Something went wrong. Please try again later.";
                traceId = Activity.Current?.Id ?? context.TraceIdentifier;
                logger.LogError(exception, "Unhandled exception: {Path}", context.Request.Path);
            }

            context.Response.StatusCode = statusCode;
            if (retryAfter != null)
                context.Response.Headers.RetryAfter = ((int)retryAfter.Value.TotalSeconds).ToString();
            await context.Response.WriteAsJsonAsync(traceId == null ? new { error } : (object)new { error, traceId }, cancellationToken);
            return true;
        }

        private static bool IsTransient(Exception exception)
        {
            if (exception is WampConnectionBrokenException or MongoConnectionException or TimeoutException
                or OperationCanceledException or HttpRequestException or SocketException)
                return true;

            return exception.InnerException != null && IsTransient(exception.InnerException);
        }
    }
}
