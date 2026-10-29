using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Interfaces;

namespace Notesnook.API.Hubs
{
    // hub connections outlive their token, so a deleted or reset user's open connection is cut off here instead
    public class SyncAuthorizationHubFilter : IHubFilter
    {
        private const string GenerationKey = "generation";

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            await EnsureCurrentAsync(invocationContext.Context, invocationContext.ServiceProvider);
            return await next(invocationContext);
        }

        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            var result = new SyncRequirement().IsAuthorized(context.Context.User, new PathString("/hubs/sync/v2"));
            if (!result.Succeeded)
            {
                var reason = result.AuthorizationFailure?.FailureReasons.FirstOrDefault();
                throw new HubException(reason?.Message ?? "Unauthorized");
            }
            await EnsureCurrentAsync(context.Context, context.ServiceProvider);
            await next(context);
        }

        // uncached, since a connection must not write under its generation once a reset has ended it
        private static async Task EnsureCurrentAsync(HubCallerContext context, IServiceProvider services)
        {
            var userId = context.User?.FindFirstValue("sub");
            if (userId == null) return;

            var repositories = services.GetRequiredService<ISyncItemsRepositoryAccessor>();
            var settings = await repositories.UsersSettings.Collection
                .Find((s) => s.UserId == userId)
                .Project((s) => new { s.DeletedAt, s.Generation })
                .FirstOrDefaultAsync();
            if (settings == null || settings.DeletedAt != null)
            {
                context.Abort();
                throw new HubException("Please login to sync.");
            }

            if (!context.Items.TryGetValue(GenerationKey, out var generation))
                context.Items[GenerationKey] = generation = settings.Generation;
            if ((long)generation! != settings.Generation)
            {
                context.Abort();
                throw new HubException("Your account was reset. Please sync again.");
            }
            repositories.Generations.Pin(userId, settings.Generation);
        }
    }
}
