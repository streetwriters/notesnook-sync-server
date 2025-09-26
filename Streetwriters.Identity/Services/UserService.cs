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
        public static SubscriptionType GetUserSubscriptionStatus(string clientId, User user)
        {
            var claimKey = GetClaimKey(clientId);
            var status = user.Claims.FirstOrDefault((c) => c.ClaimType == claimKey).ClaimValue;
            switch (status)
            {
                case "basic":
                    return SubscriptionType.BASIC;
                case "trial":
                    return SubscriptionType.TRIAL;
                case "premium":
                    return SubscriptionType.PREMIUM;
                case "premium_canceled":
                    return SubscriptionType.PREMIUM_CANCELED;
                case "premium_expired":
                    return SubscriptionType.PREMIUM_EXPIRED;
                default:
                    return SubscriptionType.BASIC;
            }
        }

        public static SubscriptionPlan GetUserSubscriptionPlan(string clientId, User user)
        {
            var claimKey = GetClaimKey(clientId);
            var status = user.Claims.FirstOrDefault((c) => c.ClaimType == claimKey).ClaimValue;
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
                    return SubscriptionPlan.FREE;
            }
        }

        public static bool IsUserPremium(string clientId, User user)
        {
            var status = GetUserSubscriptionStatus(clientId, user);
            return status == SubscriptionType.PREMIUM || status == SubscriptionType.PREMIUM_CANCELED;
        }

        public static Claim SubscriptionTypeToClaim(string clientId, SubscriptionType type)
        {
            var claimKey = GetClaimKey(clientId);
            switch (type)
            {
                case SubscriptionType.BASIC:
                    return new Claim(claimKey, "basic");
                case SubscriptionType.TRIAL:
                    return new Claim(claimKey, "trial");
                case SubscriptionType.PREMIUM:
                    return new Claim(claimKey, "premium");
                case SubscriptionType.PREMIUM_CANCELED:
                    return new Claim(claimKey, "premium_canceled");
                case SubscriptionType.PREMIUM_EXPIRED:
                    return new Claim(claimKey, "premium_expired");
            }
            return null;
        }

        public static Claim SubscriptionPlanToClaim(string clientId, SubscriptionPlan plan)
        {
            var claimKey = GetClaimKey(clientId);
            switch (plan)
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