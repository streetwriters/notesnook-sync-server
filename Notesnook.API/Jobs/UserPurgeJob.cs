using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Quartz;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Messages;

namespace Notesnook.API.Jobs
{
    [DisallowConcurrentExecution]
    public class UserPurgeJob(ISyncItemsRepositoryAccessor repositories, SyncDeviceService syncDeviceService, IS3Service s3Service, WampServiceAccessor serviceAccessor, ILogger<UserPurgeJob> logger) : IJob
    {
        // longer than a cached token's introspection (30m) and a presigned upload URL (1h), so writes made with either land before the purge
        public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(75);

        public WampServer SubscriptionServer { get; init; } = WampServers.SubscriptionServer;

        public async Task Execute(IJobExecutionContext context)
        {
            var cutoff = DateTimeOffset.UtcNow.Subtract(GracePeriod).ToUnixTimeMilliseconds();
            var userIds = await repositories.UsersSettings.Collection
                .Find(Builders<UserSettings>.Filter.Lt((s) => s.DeletedAt, cutoff))
                .Project((s) => s.UserId)
                .ToListAsync(context.CancellationToken);

            foreach (var userId in userIds)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await PurgeAsync(userId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to purge user {UserId}", userId);
                }
            }
        }

        // no transaction, since one this large outlives Mongo's time limit; every step is idempotent so a failed purge is retried
        private async Task PurgeAsync(string userId)
        {
            if (await serviceAccessor.UserAccountService.UserExistsAsync(userId))
            {
                logger.LogWarning("User {UserId} was marked for deletion but still exists", userId);
                await repositories.UsersSettings.Collection.UpdateOneAsync(
                    Builders<UserSettings>.Filter.Eq((s) => s.UserId, userId),
                    Builders<UserSettings>.Update.Unset((s) => s.DeletedAt));
                return;
            }

            logger.LogInformation("Purging data of user {UserId}", userId);

            // first, so no new inbox items arrive while purging
            await repositories.InboxApiKey.Collection.DeleteManyAsync((k) => k.UserId == userId);
            foreach (var collection in repositories.SyncItemCollections)
                await collection.Repository.Collection.DeleteManyAsync((i) => i.UserId == userId);
            await repositories.InboxItems.Collection.DeleteManyAsync((i) => i.UserId == userId);
            await repositories.Monographs.Collection.DeleteManyAsync((m) => m.UserId == userId);
            await syncDeviceService.ResetDevicesAsync(userId);
            await s3Service.DeleteDirectoryAsync(userId);
            if (!Constants.IS_SELF_HOSTED)
            {
                await SubscriptionServer.PublishMessageAsync(SubscriptionServerTopics.DeleteSubscriptionTopic, new DeleteSubscriptionMessage
                {
                    AppId = ApplicationType.NOTESNOOK,
                    UserId = userId
                });
            }

            // last, as it marks the purge as unfinished
            await repositories.UsersSettings.Collection.DeleteOneAsync((s) => s.UserId == userId);
            logger.LogInformation("Purged data of user {UserId}", userId);
        }
    }
}
