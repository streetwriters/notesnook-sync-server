using System.Threading;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace System.Security.Claims
{
    public static class ClaimsPrincipalExtensions
    {
        private readonly static string[] SUBSCRIBED_CLAIMS = ["believer", "education", "essential", "pro", "legacy_pro"];
        public static bool IsUserSubscribed(this ClaimsPrincipal user)
         => user.Claims.Any((c) => c.Type == "notesnook:status" && SUBSCRIBED_CLAIMS.Contains(c.Value));
    }
}