using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;

namespace Notesnook.API.Hubs
{
    // hub connections outlive their token, so a deleted or reset user's open connection is cut off here instead
    public class SyncAuthorizationHubFilter(ILogger<SyncAuthorizationHubFilter> logger) : IHubFilter
    {
        private const string GenerationKey = "generation";

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            await EnsureCurrentAsync(invocationContext.Context, invocationContext.ServiceProvider);
            return await next(invocationContext);
        }

        // HubConnectionHandler's own logging is turned off in appsettings.json: it logs every rejection below
        // (e.g. unconfirmed emails) as an error, so connection failures are logged and counted here instead.
        // Rejections are logged at Information, which production filters out, so the counter is what shows them.
        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            var stage = "unauthorized";
            try
            {
                var result = new SyncRequirement().IsAuthorized(context.Context.User, new PathString("/hubs/sync/v2"));
                if (!result.Succeeded)
                {
                    var reason = result.AuthorizationFailure?.FailureReasons.FirstOrDefault();
                    // clients read this message from the close error, so it must stay a HubException
                    throw new HubException(reason?.Message ?? "Unauthorized");
                }
                stage = "account";
                await EnsureCurrentAsync(context.Context, context.ServiceProvider);
                stage = "hub";
                await next(context);
            }
            catch (HubException ex)
            {
                SyncEventCounterSource.Log.ConnectionRejected(stage);
                logger.LogInformation("Connection {ConnectionId} rejected: {Reason}", context.Context.ConnectionId, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                SyncEventCounterSource.Log.ConnectionRejected("error");
                logger.LogError(ex, "OnConnectedAsync failed for connection {ConnectionId}", context.Context.ConnectionId);
                throw;
            }
        }

        private sealed class AccountState
        {
            public long? DeletedAt { get; set; }
            public long Generation { get; set; }
        }

        // built once: a LINQ projection to an anonymous type rebuilt its class map and compiled a constructor on every call
        private static readonly ProjectionDefinition<UserSettings, AccountState> AccountStateProjection =
            new BsonDocument { { "_id", 0 }, { nameof(UserSettings.DeletedAt), 1 }, { nameof(UserSettings.Generation), 1 } };

        // uncached, since a connection must not write under its generation once a reset has ended it
        private static async Task EnsureCurrentAsync(HubCallerContext context, IServiceProvider services)
        {
            var userId = context.User?.FindFirstValue("sub");
            if (userId == null) return;

            var repositories = services.GetRequiredService<ISyncItemsRepositoryAccessor>();
            var settings = await repositories.UsersSettings.Collection
                .Find(Builders<UserSettings>.Filter.Eq(nameof(UserSettings.UserId), userId))
                .Project(AccountStateProjection)
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
