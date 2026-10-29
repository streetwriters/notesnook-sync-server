using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Interfaces;

namespace Notesnook.API.Hubs
{
    // hub connections outlive their token, so a deleted user's open connection is cut off here instead
    public class SyncAuthorizationHubFilter(IMemoryCache cache) : IHubFilter
    {
        // a stale answer only lets through writes that land well before UserPurgeJob's grace period ends
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            await EnsureNotDeletedAsync(invocationContext.Context, invocationContext.ServiceProvider);
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
            await EnsureNotDeletedAsync(context.Context, context.ServiceProvider);
            await next(context);
        }

        private async Task EnsureNotDeletedAsync(HubCallerContext context, IServiceProvider services)
        {
            var userId = context.User?.FindFirstValue("sub");
            if (userId == null) return;

            var deleted = await cache.GetOrCreateAsync($"user_deleted:{userId}", async (entry) =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return !await services.GetRequiredService<ISyncItemsRepositoryAccessor>().UsersSettings.Collection
                    .Find((s) => s.UserId == userId && s.DeletedAt == null).AnyAsync();
            });
            if (!deleted) return;

            context.Abort();
            throw new HubException("Please login to sync.");
        }
    }
}
