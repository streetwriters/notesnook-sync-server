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
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Identity.Controllers;

namespace System.Collections.Generic
{
    public static class IEnumberableExtensions
    {
        public static IEnumerable<string> ToErrors(this IEnumerable<IdentityError> collection)
        {
            return collection.Select((e) => e.Description);
        }

        public static string GetClaimValue(this IEnumerable<Claim> claims, string type)
        {
            return claims.FirstOrDefault((c) => c.Type == type)?.Value;
        }
    }
}