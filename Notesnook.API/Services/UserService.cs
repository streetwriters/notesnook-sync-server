/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Notesnook.API.Helpers;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Models.Responses;
using Notesnook.API.Repositories;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Exceptions;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Services
{
    public class UserService(IHttpContextAccessor accessor,
        ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor,
        IUnitOfWork unitOfWork, IS3Service s3Service, SyncDeviceService syncDeviceService, WampServiceAccessor serviceAccessor, ILogger<UserService> logger) : IUserService
    {
        private static readonly System.Security.Cryptography.RandomNumberGenerator Rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        private IHttpContextAccessor HttpContextAccessor { get; } = accessor;
        private ISyncItemsRepositoryAccessor Repositories { get; } = syncItemsRepositoryAccessor;
        private IS3Service S3Service { get; set; } = s3Service;
        private readonly IUnitOfWork unit = unitOfWork;

        public async Task<SignupResponse> CreateUserAsync(SignupForm form)
        {
            SignupResponse response = await serviceAccessor.UserAccountService.CreateUserAsync(form.ClientId, form.Email, form.Password, HttpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString());

            if ((response.Errors != null && response.Errors.Length > 0) || response.UserId == null)
            {
                logger.LogError("Failed to sign up user: {Response}", JsonSerializer.Serialize(response));
                if (response.Errors != null && response.Errors.Length > 0)
                    throw new ClientException(string.Join(" ", response.Errors));
                else throw new ClientException("Could not create a new account.");
            }

            try
            {
                await Repositories.UsersSettings.InsertAsync(new UserSettings
                {
                    UserId = response.UserId,
                    StorageLimit = new Limit { UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Value = 0 },
                    LastSynced = 0,
                    Salt = GetSalt()
                });
            }
            // until Identity's users are unique, a double-submitted signup can get the same user twice
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }

            if (!Constants.IS_SELF_HOSTED)
            {
                // awaited rather than published so the client's first user fetch already sees the subscription
                await serviceAccessor.UserSubscriptionService.CreateSubscriptionAsync(new CreateSubscriptionMessageV2
                {
                    AppId = ApplicationType.NOTESNOOK,
                    Provider = SubscriptionProvider.STREETWRITERS,
                    Status = SubscriptionStatus.ACTIVE,
                    Plan = SubscriptionPlan.FREE,
                    UserId = response.UserId,
                    StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
            }

            return response;
        }

        public async Task<UserResponse> GetUserAsync(string userId)
        {
            var user = await serviceAccessor.UserAccountService.GetUserAsync(Clients.Notesnook.Id, userId) ?? throw new NotFoundException("User not found.");

            Subscription? subscription = null;
            if (Constants.IS_SELF_HOSTED)
            {
                subscription = new Subscription
                {
                    AppId = ApplicationType.NOTESNOOK,
                    Provider = SubscriptionProvider.STREETWRITERS,
                    Plan = SubscriptionPlan.BELIEVER,
                    Status = SubscriptionStatus.ACTIVE,
                    UserId = user.UserId,
                    StartDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    // this date doesn't matter as the subscription is static.
                    ExpiryDate = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeMilliseconds()
                };
            }
            else
            {
                subscription = await serviceAccessor.UserSubscriptionService.GetUserSubscriptionAsync(Clients.Notesnook.Id, userId) ?? throw new ClientException("User subscription not found.");
            }

            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == user.UserId) ?? throw new Exception("User settings not found.");

            // reset user's attachment limit every month
            var limit = StorageHelper.RolloverStorageLimit(userSettings.StorageLimit);
            if (userSettings.StorageLimit == null || limit.UpdatedAt != userSettings.StorageLimit?.UpdatedAt)
            {
                // only roll over the usage we read, so a concurrent upload's increment isn't overwritten
                var observedFilter = userSettings.StorageLimit == null
                    ? Builders<UserSettings>.Filter.Eq(u => u.StorageLimit, null)
                    : Builders<UserSettings>.Filter.Eq("StorageLimit.UpdatedAt", userSettings.StorageLimit.UpdatedAt);
                userSettings.StorageLimit = limit;
                await Repositories.UsersSettings.Collection.UpdateOneAsync(
                    Builders<UserSettings>.Filter.And(Builders<UserSettings>.Filter.Eq(u => u.UserId, user.UserId), observedFilter),
                    Builders<UserSettings>.Update.Set(u => u.StorageLimit, userSettings.StorageLimit)
                );
            }

            return new UserResponse
            {
                UserId = user.UserId,
                Email = user.Email,
                IsEmailConfirmed = user.IsEmailConfirmed,
                MarketingConsent = user.MarketingConsent,
                MFA = user.MFA,
                PhoneNumber = user.PhoneNumber,
                AttachmentsKey = userSettings.AttachmentsKey,
                MonographPasswordsKey = userSettings.MonographPasswordsKey,
                DataEncryptionKey = userSettings.DataEncryptionKey,
                LegacyDataEncryptionKey = userSettings.LegacyDataEncryptionKey,
                InboxKeys = userSettings.InboxKeys,
                Revision = userSettings.Revision,
                PendingKeys = userSettings.PendingKeys,
                Salt = userSettings.Salt,
                Subscription = subscription,
                StorageUsed = userSettings.StorageLimit.Value,
                TotalStorage = StorageHelper.GetStorageLimitForPlan(subscription),
                Success = true,
                StatusCode = 200
            };
        }

        public async Task<EncryptedData?> GetEncryptionVerifier(string userId)
        {
            // an item from before a reset is encrypted with the old key, so it would make the client reject its new key
            var generation = await Repositories.Generations.GetAsync(userId);
            var filter = Builders<SyncItem>.Filter.And(
                Builders<SyncItem>.Filter.Eq((s) => s.UserId, userId),
                Builders<SyncItem>.Filter.In((s) => s.KeyVersion, [null, 0]),
                UserGenerations.Filter(generation));
            foreach (var collection in Repositories.SyncItemCollections)
            {
                var item = await collection.Repository.Collection.Find(filter).FirstOrDefaultAsync();
                if (item != null)
                {
                    return new EncryptedData
                    {
                        Cipher = item.Cipher,
                        IV = item.IV,
                        Salt = "",
                        Length = item.Length
                    };
                }
            }
            return null;
        }

        public async Task DeleteUserAsync(string userId)
        {
            logger.LogInformation("Deleting user {UserId}", userId);

            // marked first so a user gone from Identity is never left unpurged; upserted so a user without settings is still purged
            try
            {
                await Repositories.UsersSettings.Collection.UpdateOneAsync(
                    Builders<UserSettings>.Filter.Eq((s) => s.UserId, userId),
                    Builders<UserSettings>.Update.Set((s) => s.DeletedAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                    new UpdateOptions { IsUpsert = true });
            }
            // a concurrent delete upserted the settings first, so the user is already marked
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }

            await serviceAccessor.UserAccountService.UnregisterUserAsync(Clients.Notesnook.Id, userId);
        }

        public async Task DeleteUserAsync(string userId, string? jti, string password)
        {
            // Identity's own "Wrong password." error only reaches us as a WAMP runtime error
            if (!await serviceAccessor.UserAccountService.VerifyPasswordAsync(userId, password))
                throw new ClientException("Wrong password.");

            await DeleteUserAsync(userId);

            await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
            {
                SendToAll = jti == null,
                OriginTokenId = jti,
                UserId = userId,
                Message = new Message
                {
                    Type = "logout",
                    Data = JsonSerializer.Serialize(new { reason = "Account deleted." })
                }
            });

        }

        public async Task ResetUserAsync(string userId, bool removeAttachments)
        {
            // small enough to delete right away, and before the generation changes, so a failure leaves nothing reset
            Repositories.Monographs.DeleteMany((m) => m.UserId == userId);
            Repositories.InboxApiKey.DeleteMany((t) => t.UserId == userId);
            Repositories.InboxItems.DeleteMany((i) => i.UserId == userId);
            await unit.Commit();
            await syncDeviceService.ResetDevicesAsync(userId);

            var result = await Repositories.UsersSettings.Collection.UpdateOneAsync(Builders<UserSettings>.Filter.Eq((s) => s.UserId, userId), Builders<UserSettings>.Update
                .Set((s) => s.AttachmentsKey, null)
                .Set((s) => s.MonographPasswordsKey, null)
                .Set((s) => s.DataEncryptionKey, null)
                .Set((s) => s.LegacyDataEncryptionKey, null)
                .Set((s) => s.VaultKey, null)
                .Set((s) => s.InboxKeys, null)
                .Set((s) => s.PendingKeys, null)
                .Set((s) => s.PendingKeysSince, null)
                .Set((s) => s.LastSynced, 0)
                .Inc((s) => s.Revision, 1)
                .Inc((s) => s.Generation, 1)
                .Set((s) => s.PurgePendingSince, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            if (result.MatchedCount == 0) throw new NotFoundException("User not found.");

            if (removeAttachments)
                await DeleteAttachmentsAsync(userId);
        }

        // runs after the user's data is already gone, so a failure here must not turn a completed deletion into an error
        private async Task DeleteAttachmentsAsync(string userId)
        {
            try
            {
                await S3Service.DeleteDirectoryAsync(userId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete attachments of user {UserId}", userId);
            }
        }

        private static string GetSalt()
        {
            byte[] salt = new byte[16];
            Rng.GetNonZeroBytes(salt);
            return Convert.ToBase64String(salt).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}