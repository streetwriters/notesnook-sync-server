using System.Collections.Generic;
using Notesnook.API.Models;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;

namespace Notesnook.API.Helpers
{
    class StorageHelper
    {
        const long MB = 1024 * 1024;
        const long GB = 1024 * MB;
        public readonly static Dictionary<SubscriptionPlan, long> MAX_STORAGE_PER_MONTH = new()
            {
                { SubscriptionPlan.FREE, 50L * MB },
                { SubscriptionPlan.ESSENTIAL, GB },
                { SubscriptionPlan.PRO, 10L * GB },
                { SubscriptionPlan.EDUCATION, 10L * GB },
                { SubscriptionPlan.BELIEVER, 25L * GB },
                { SubscriptionPlan.LEGACY_PRO, -1 }
            };
        public readonly static Dictionary<SubscriptionPlan, long> MAX_FILE_SIZE = new()
            {
                { SubscriptionPlan.FREE, 10 * MB },
                { SubscriptionPlan.ESSENTIAL, 100 * MB },
                { SubscriptionPlan.PRO, 1L * GB },
                { SubscriptionPlan.EDUCATION, 1L * GB },
                { SubscriptionPlan.BELIEVER, 5L * GB },
                { SubscriptionPlan.LEGACY_PRO, 512 * MB }
            };

        public static long GetStorageLimitForPlan(Subscription subscription)
        {
            return MAX_STORAGE_PER_MONTH[subscription.Plan];
        }

        public static bool IsStorageLimitReached(Subscription subscription, Limit limit)
        {
            var storageLimit = GetStorageLimitForPlan(subscription);
            if (storageLimit == -1) return false;
            return limit.Value > storageLimit;
        }

        public static bool IsFileSizeExceeded(Subscription subscription, long fileSize)
        {
            var maxFileSize = MAX_FILE_SIZE[subscription.Plan];
            return fileSize > maxFileSize;
        }
    }
}