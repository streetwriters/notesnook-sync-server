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
using Streetwriters.Common;
using Streetwriters.Identity.Controllers;
using Streetwriters.Identity.Enums;

namespace Streetwriters.Identity.Extensions
{
    public static class UrlExtensions
    {
        public static string? TokenLink(string userId, string code, string clientId, TokenType type)
        {
            var url = new UriBuilder();
#if (DEBUG || STAGING)
            url.Host = $"{Servers.IdentityServer.Hostname}";
            url.Port = Servers.IdentityServer.Port;
            url.Scheme = "http";
#else
            url.Host = Servers.IdentityServer.PublicURL.Host;
            url.Scheme = Servers.IdentityServer.PublicURL.Scheme;
#endif
            url.Path = "account/confirm";
            url.Query = $"userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}&clientId={Uri.EscapeDataString(clientId)}&type={Uri.EscapeDataString(type.ToString())}";
            return url.ToString();
        }
    }
}