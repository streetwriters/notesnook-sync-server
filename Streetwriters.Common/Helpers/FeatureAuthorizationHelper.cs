using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using WebMarkupMin.Core;
using WebMarkupMin.Core.Loggers;

namespace Streetwriters.Common.Helpers
{
    public enum Features
    {
        SMS_2FA,
        MONOGRAPH_ANALYTICS
    }

    public static class FeatureAuthorizationHelper
    {
        private static SubscriptionPlan? GetUserSubscriptionPlan(string clientId, ClaimsPrincipal user)
        {
            var claimKey = $"{clientId}:status";
            var status = user.FindFirstValue(claimKey);
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

        public static bool IsFeatureAllowed(Features feature, string clientId, ClaimsPrincipal user)
        {
            if (Constants.IS_SELF_HOSTED)
                return true;

            var status = GetUserSubscriptionPlan(clientId, user);

            switch (feature)
            {
                case Features.SMS_2FA:
                case Features.MONOGRAPH_ANALYTICS:
                    return status == SubscriptionPlan.LEGACY_PRO ||
                            status == SubscriptionPlan.PRO ||
                            status == SubscriptionPlan.EDUCATION ||
                            status == SubscriptionPlan.BELIEVER;
                default:
                    return false;
            }
        }
    }
}
