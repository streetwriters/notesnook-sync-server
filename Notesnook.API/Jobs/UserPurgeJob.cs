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
        // a token cached at deletion (30m) can still start a 5 GiB upload (90m) or get a presigned upload URL (1h), so those land before the purge
        public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(150);
        // requests that read the old generation before a reset can still write to it, so those land before the purge
        public static readonly TimeSpan ResetGracePeriod = TimeSpan.FromMinutes(15);

        public WampServer SubscriptionServer { get; init; } = WampServers.SubscriptionServer;

        public async Task Execute(IJobExecutionContext context)
        {
            SyncEventCounterSource.Log.PurgeStarted();
            var cutoff = DateTimeOffset.UtcNow.Subtract(GracePeriod).ToUnixTimeMilliseconds();
            var users = await repositories.UsersSettings.Collection
                .Find(Builders<UserSettings>.Filter.Lt((s) => s.DeletedAt, cutoff))
                .Project((s) => new { s.UserId, s.DeletedAt })
                .ToListAsync(context.CancellationToken);

            foreach (var user in users)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await PurgeAsync(user.UserId, user.DeletedAt!.Value);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to purge user {UserId}", user.UserId);
                    SyncEventCounterSource.Log.PurgeFailed();
                }
            }

            var resets = await repositories.UsersSettings.Collection
                .Find(Builders<UserSettings>.Filter.And(
                    Builders<UserSettings>.Filter.Lt((s) => s.PurgePendingSince, DateTimeOffset.UtcNow.Subtract(ResetGracePeriod).ToUnixTimeMilliseconds()),
                    Builders<UserSettings>.Filter.Eq((s) => s.DeletedAt, null)))
                .Project((s) => new { s.UserId, s.Generation })
                .ToListAsync(context.CancellationToken);

            foreach (var user in resets)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await PurgeResetAsync(user.UserId, user.Generation);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to purge the reset data of user {UserId}", user.UserId);
                    SyncEventCounterSource.Log.PurgeFailed();
                }
            }
        }

        private async Task PurgeResetAsync(string userId, long generation)
        {
            logger.LogInformation("Purging reset data of user {UserId}", userId);
            foreach (var collection in repositories.SyncItemCollections)
                await collection.Repository.DeleteOlderGenerationsAsync(userId, generation);

            // only the generation we purged, so a reset made meanwhile stays pending
            await repositories.UsersSettings.Collection.UpdateOneAsync(
                (s) => s.UserId == userId && s.Generation == generation,
                Builders<UserSettings>.Update.Unset((s) => s.PurgePendingSince));
            logger.LogInformation("Purged reset data of user {UserId}", userId);
        }

        // no transaction, since one this large outlives Mongo's time limit; every step is idempotent so a failed purge is retried
        private async Task PurgeAsync(string userId, long deletedAt)
        {
            if (await serviceAccessor.UserAccountService.UserExistsAsync(userId))
            {
                logger.LogWarning("User {UserId} was marked for deletion but still exists", userId);
                // only the mark we saw, so a delete retried meanwhile keeps its own
                await repositories.UsersSettings.Collection.UpdateOneAsync(
                    (s) => s.UserId == userId && s.DeletedAt == deletedAt,
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
