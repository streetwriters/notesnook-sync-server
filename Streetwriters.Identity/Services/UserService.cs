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

using System.Linq;
using System.Security.Claims;
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



        public static bool IsUserPremium(string clientId, User user)
        {
            var status = GetUserSubscriptionStatus(clientId, user);
            string[] allowedClaims = { "trial", "premium", "premium_canceled" };

            return status == SubscriptionType.TRIAL || status == SubscriptionType.PREMIUM || status == SubscriptionType.PREMIUM_CANCELED;
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

        public static string GetClaimKey(string clientId)
        {
            return $"{clientId}:status";
        }
    }
}