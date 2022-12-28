using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Notesnook.API.Authorization
{
    public class ProUserRequirement : AuthorizationHandler<ProUserRequirement>, IAuthorizationRequirement
    {
        private string[] allowedClaims = { "trial", "premium", "premium_canceled" };
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ProUserRequirement requirement)
        {
            var isProOrTrial = context.User.HasClaim((c) => c.Type == "notesnook:status" && allowedClaims.Contains(c.Value));
            if (isProOrTrial)
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}