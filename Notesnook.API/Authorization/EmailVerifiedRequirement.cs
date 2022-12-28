using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Notesnook.API.Authorization
{
    public class EmailVerifiedRequirement : AuthorizationHandler<EmailVerifiedRequirement>, IAuthorizationRequirement
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EmailVerifiedRequirement requirement)
        {
            var isEmailVerified = context.User.HasClaim("verified", "true");
            var isUserBasic = context.User.HasClaim("notesnook:status", "basic") || context.User.HasClaim("notesnook:status", "premium_expired");
            if (!isUserBasic || isEmailVerified)
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}