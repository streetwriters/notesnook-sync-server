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
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Models.Responses;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Services
{
    public class UserService : IUserService
    {
        private static readonly System.Security.Cryptography.RandomNumberGenerator Rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        private readonly HttpClient httpClient;
        private IHttpContextAccessor HttpContextAccessor { get; }
        private ISyncItemsRepositoryAccessor Repositories { get; }
        private IS3Service S3Service { get; set; }
        private readonly IUnitOfWork unit;

        public UserService(IHttpContextAccessor accessor,
        ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor,
        IUnitOfWork unitOfWork, IS3Service s3Service)
        {
            httpClient = new HttpClient();

            Repositories = syncItemsRepositoryAccessor;
            HttpContextAccessor = accessor;
            unit = unitOfWork;
            S3Service = s3Service;
        }

        public async Task CreateUserAsync()
        {
            SignupResponse response = await httpClient.ForwardAsync<SignupResponse>(this.HttpContextAccessor, $"{Servers.IdentityServer}/signup", HttpMethod.Post);
            if (!response.Success || (response.Errors != null && response.Errors.Length > 0))
            {
                await Slogger<UserService>.Error(nameof(CreateUserAsync), "Couldn't sign up.", JsonSerializer.Serialize(response));
                if (response.Errors != null && response.Errors.Length > 0)
                    throw new Exception(string.Join(" ", response.Errors));
                else throw new Exception("Could not create a new account. Error code: " + response.StatusCode);
            }

            await Repositories.UsersSettings.InsertAsync(new UserSettings
            {
                UserId = response.UserId,
                LastSynced = 0,
                Salt = GetSalt()
            });

            if (!Constants.IS_SELF_HOSTED)
            {
                await WampServers.SubscriptionServer.PublishMessageAsync(SubscriptionServerTopics.CreateSubscriptionTopic, new CreateSubscriptionMessage
                {
                    AppId = ApplicationType.NOTESNOOK,
                    Provider = SubscriptionProvider.STREETWRITERS,
                    Type = SubscriptionType.BASIC,
                    UserId = response.UserId,
                    StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
            }

            await Slogger<UserService>.Info(nameof(CreateUserAsync), "New user created.", JsonSerializer.Serialize(response));
        }

        public async Task<UserResponse> GetUserAsync(string userId)
        {
            var userService = await WampServers.IdentityServer.GetServiceAsync<IUserAccountService>(IdentityServerTopics.UserAccountServiceTopic);

            var user = await userService.GetUserAsync(Clients.Notesnook.Id, userId) ?? throw new Exception("User not found.");

            ISubscription subscription = null;
            if (Constants.IS_SELF_HOSTED)
            {
                subscription = new Subscription
                {
                    AppId = ApplicationType.NOTESNOOK,
                    Provider = SubscriptionProvider.STREETWRITERS,
                    Type = SubscriptionType.PREMIUM,
                    UserId = user.UserId,
                    StartDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    // this date doesn't matter as the subscription is static.
                    ExpiryDate = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeMilliseconds()
                };
            }
            else
            {
                var subscriptionService = await WampServers.SubscriptionServer.GetServiceAsync<IUserSubscriptionService>(SubscriptionServerTopics.UserSubscriptionServiceTopic);
                subscription = await subscriptionService.GetUserSubscriptionAsync(Clients.Notesnook.Id, userId);
            }

            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == user.UserId) ?? throw new Exception("User settings not found.");
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
                Salt = userSettings.Salt,
                Subscription = subscription,
                Success = true,
                StatusCode = 200
            };
        }

        public async Task SetUserKeysAsync(string userId, UserKeys keys)
        {
            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId) ?? throw new Exception("User not found.");

            if (keys.AttachmentsKey != null)
            {
                userSettings.AttachmentsKey = keys.AttachmentsKey;
            }
            if (keys.MonographPasswordsKey != null)
            {
                userSettings.MonographPasswordsKey = keys.MonographPasswordsKey;
            }

            await Repositories.UsersSettings.UpdateAsync(userSettings.Id, userSettings);
        }

        public async Task DeleteUserAsync(string userId)
        {
            new SyncDeviceService(new SyncDevice(userId, userId)).ResetDevices();

            var cc = new CancellationTokenSource();

            Repositories.Notes.DeleteByUserId(userId);
            Repositories.Notebooks.DeleteByUserId(userId);
            Repositories.Shortcuts.DeleteByUserId(userId);
            Repositories.Contents.DeleteByUserId(userId);
            Repositories.Settings.DeleteByUserId(userId);
            Repositories.LegacySettings.DeleteByUserId(userId);
            Repositories.Attachments.DeleteByUserId(userId);
            Repositories.Reminders.DeleteByUserId(userId);
            Repositories.Relations.DeleteByUserId(userId);
            Repositories.Colors.DeleteByUserId(userId);
            Repositories.Tags.DeleteByUserId(userId);
            Repositories.Vaults.DeleteByUserId(userId);
            Repositories.UsersSettings.Delete((u) => u.UserId == userId);
            Repositories.Monographs.DeleteMany((m) => m.UserId == userId);

            var result = await unit.Commit();
            await Slogger<UserService>.Info(nameof(DeleteUserAsync), "User data deleted", userId, result.ToString());
            if (!result) throw new Exception("Could not delete user data.");

            if (!Constants.IS_SELF_HOSTED)
            {
                await WampServers.SubscriptionServer.PublishMessageAsync(SubscriptionServerTopics.DeleteSubscriptionTopic, new DeleteSubscriptionMessage
                {
                    AppId = ApplicationType.NOTESNOOK,
                    UserId = userId
                });
            }

            await S3Service.DeleteDirectoryAsync(userId);
        }

        public async Task DeleteUserAsync(string userId, string jti, string password)
        {
            await Slogger<UserService>.Info(nameof(DeleteUserAsync), "Deleting user account", userId);

            var userService = await WampServers.IdentityServer.GetServiceAsync<IUserAccountService>(IdentityServerTopics.UserAccountServiceTopic);
            await userService.DeleteUserAsync(Clients.Notesnook.Id, userId, password);

            await DeleteUserAsync(userId);

            await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
            {
                SendToAll = false,
                OriginTokenId = jti,
                UserId = userId,
                Message = new Message
                {
                    Type = "logout",
                    Data = JsonSerializer.Serialize(new { reason = "Account deleted." })
                }
            });

        }

        public async Task<bool> ResetUserAsync(string userId, bool removeAttachments)
        {
            new SyncDeviceService(new SyncDevice(userId, userId)).ResetDevices();

            var cc = new CancellationTokenSource();

            Repositories.Notes.DeleteByUserId(userId);
            Repositories.Notebooks.DeleteByUserId(userId);
            Repositories.Shortcuts.DeleteByUserId(userId);
            Repositories.Contents.DeleteByUserId(userId);
            Repositories.Settings.DeleteByUserId(userId);
            Repositories.LegacySettings.DeleteByUserId(userId);
            Repositories.Attachments.DeleteByUserId(userId);
            Repositories.Reminders.DeleteByUserId(userId);
            Repositories.Relations.DeleteByUserId(userId);
            Repositories.Colors.DeleteByUserId(userId);
            Repositories.Tags.DeleteByUserId(userId);
            Repositories.Vaults.DeleteByUserId(userId);
            Repositories.Monographs.DeleteMany((m) => m.UserId == userId);
            if (!await unit.Commit()) return false;

            var userSettings = await Repositories.UsersSettings.FindOneAsync((s) => s.UserId == userId);

            userSettings.AttachmentsKey = null;
            userSettings.MonographPasswordsKey = null;
            userSettings.VaultKey = null;
            userSettings.LastSynced = 0;

            await Repositories.UsersSettings.UpsertAsync(userSettings, (s) => s.UserId == userId);

            if (removeAttachments)
                await S3Service.DeleteDirectoryAsync(userId);

            return true;
        }

        private static string GetSalt()
        {
            byte[] salt = new byte[16];
            Rng.GetNonZeroBytes(salt);
            return Convert.ToBase64String(salt).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}