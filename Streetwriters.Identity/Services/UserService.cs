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
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Services
{
    public class UserService
    {
        private static SubscriptionPlan? GetUserSubscriptionPlan(string clientId, User user)
        {
            var claimKey = GetClaimKey(clientId);
            var status = user.Claims.FirstOrDefault((c) => c.ClaimType == claimKey)?.ClaimValue;
            switch (status)
            {
                case "free":
                    return SubscriptionPlan.FREE;
                case "believer":
                    return SubscriptionPlan.BELIEVER;
                case "education":
                    return SubscriptionPlan.EDUCATION;
                case "essential":
                    return SubscriptionPlan.ESSENTIAL;
                case "pro":
                    return SubscriptionPlan.PRO;
                default:
                    return null;
            }
        }

        public static bool IsSMSMFAAllowed(string clientId, User user)
        {
            var status = GetUserSubscriptionPlan(clientId, user);
            if (status == null) return false;
            return status == SubscriptionPlan.LEGACY_PRO ||
                    status == SubscriptionPlan.PRO ||
                    status == SubscriptionPlan.EDUCATION ||
                    status == SubscriptionPlan.BELIEVER;
        }

        public static Claim SubscriptionPlanToClaim(string clientId, Subscription subscription)
        {
            var claimKey = GetClaimKey(clientId);

            // just in case
            if (subscription.ExpiryDate <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return new Claim(claimKey, "free");

            switch (subscription.Plan)
            {
                case SubscriptionPlan.FREE:
                    return new Claim(claimKey, "free");
                case SubscriptionPlan.BELIEVER:
                    return new Claim(claimKey, "believer");
                case SubscriptionPlan.EDUCATION:
                    return new Claim(claimKey, "education");
                case SubscriptionPlan.ESSENTIAL:
                    return new Claim(claimKey, "essential");
                case SubscriptionPlan.PRO:
                    return new Claim(claimKey, "pro");
                case SubscriptionPlan.LEGACY_PRO:
                    return new Claim(claimKey, "legacy_pro");
            }
            return null;
        }

        public static string GetClaimKey(string clientId)
        {
            return $"{clientId}:status";
        }

        public static async Task<bool> IsUserValidAsync(UserManager<User> userManager, User user, string clientId)
        {
            return user != null && await userManager.IsInRoleAsync(user, clientId);
        }
    }
}