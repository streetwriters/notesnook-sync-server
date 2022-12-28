using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Notesnook.API.Authorization
{
    public class NotesnookUserRequirement : AuthorizationHandler<NotesnookUserRequirement>, IAuthorizationRequirement
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, NotesnookUserRequirement requirement)
        {
            var isInAudience = context.User.HasClaim("aud", "notesnook");
            var hasRole = context.User.HasClaim("role", "notesnook");
            if (isInAudience && hasRole)
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}