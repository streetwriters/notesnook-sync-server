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
using System.Net;
using System.Text;
using Ng.Services;

namespace Microsoft.AspNetCore.Http
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Get remote ip address, optionally allowing for x-forwarded-for header check
        /// </summary>
        /// <param name="context">Http context</param>
        /// <param name="allowForwarded">Whether to allow x-forwarded-for header check</param>
        /// <returns>IPAddress</returns>
        public static IPAddress? GetRemoteIPAddress(this HttpContext context, bool allowForwarded = true)
        {
            var remote = context.Connection.RemoteIpAddress;
            // only a proxy in front of the server may say who the client is; a client reaching it directly could put any address here
            if (allowForwarded && remote != null && IsPrivateOrLoopback(remote))
            {
                string? header = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
                if (IPAddress.TryParse(header, out IPAddress? ip))
                {
                    return ip;
                }
            }
            return remote;
        }

        private static bool IsPrivateOrLoopback(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return address.IsIPv6UniqueLocal || address.IsIPv6LinkLocal;
            var b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        static readonly UserAgentService userAgentService = new();
        public static string GetClientInfo(this HttpContext httpContext)
        {
            var clientIp = httpContext.GetRemoteIPAddress()?.ToString();
            var country = httpContext.Request.Headers["CF-IPCountry"];
            var userAgent = httpContext.Request.Headers.UserAgent;
            var builder = new StringBuilder();

            builder.AppendLine($"Date: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")}");

            if (!string.IsNullOrEmpty(country))
                builder.AppendLine($"IP: {clientIp}");

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