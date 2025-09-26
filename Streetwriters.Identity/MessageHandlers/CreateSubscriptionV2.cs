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
using System.Security.Claims;
using System.Linq;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.MessageHandlers
{
    public class CreateSubscriptionV2
    {
        public static async Task Process(CreateSubscriptionMessageV2 message, UserManager<User> userManager)
        {
            var user = await userManager.FindByIdAsync(message.UserId);
            var client = Clients.FindClientByAppId(message.AppId);
            if (client == null || user == null) return;

            IdentityUserClaim<string> statusClaim = user.Claims.FirstOrDefault((c) => c.ClaimType == UserService.GetClaimKey(client.Id));
            Claim subscriptionClaim = UserService.SubscriptionPlanToClaim(client.Id, message.Plan);
            if (statusClaim?.ClaimValue == subscriptionClaim.Value) return;
            if (statusClaim != null)
                await userManager.ReplaceClaimAsync(user, statusClaim.ToClaim(), subscriptionClaim);
            else
                await userManager.AddClaimAsync(user, subscriptionClaim);
        }

    }
}