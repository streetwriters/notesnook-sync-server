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

using System.Threading.Tasks;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Common;
using Microsoft.AspNetCore.Identity;
using System.Linq;

namespace Streetwriters.Identity.MessageHandlers
{
    public class DeleteSubscription
    {
        public static async Task Process(DeleteSubscriptionMessage message, UserManager<User> userManager)
        {
            var user = await userManager.FindByIdAsync(message.UserId);
            var client = Clients.FindClientByAppId(message.AppId);
            if (client == null || user == null) return;

            IdentityUserClaim<string> statusClaim = user.Claims.FirstOrDefault((c) => c.ClaimType == $"{client.Id}:status");
            if (statusClaim != null)
            {
                await userManager.RemoveClaimAsync(user, statusClaim.ToClaim());
            }
        }
    }
}
