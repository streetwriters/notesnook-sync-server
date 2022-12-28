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

using System;
using System.Text;
using Ng.Services;

namespace Microsoft.AspNetCore.Http
{
    public static class HttpContextExtensions
    {
        static UserAgentService userAgentService = new UserAgentService();
        public static string GetClientInfo(this HttpContext httpContext)
        {
            var clientIp = httpContext.Connection.RemoteIpAddress;
            var country = httpContext.Request.Headers["CF-IPCountry"];
            var userAgent = httpContext.Request.Headers["User-Agent"];
            var builder = new StringBuilder();

            builder.AppendLine($"Date: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")}");

            if (clientIp != null)
                builder.AppendLine($"IP: {clientIp.ToString()}");

            if (!string.IsNullOrEmpty(country))
                builder.AppendLine($"Country: {country.ToString()}");

            if (!string.IsNullOrEmpty(userAgent))
            {
                var ua = userAgentService.Parse(userAgent);
                if (!string.IsNullOrEmpty(ua.Browser))
                    builder.AppendLine($"Browser: {ua.Browser} {ua.BrowserVersion}");
                if (!string.IsNullOrEmpty(ua.Platform))
                    builder.AppendLine($"Platform: {ua.Platform}");
            }

            return builder.ToString();
        }
    }
}