using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IdentityServer4;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Services
{
    public class UserAccountService(UserManager<User> userManager, IMFAService mfaService, IPersistedGrantStore persistedGrantStore) : IUserAccountService
    {
        public async Task<UserModel?> GetUserAsync(string clientId, string userId)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null || !await UserService.IsUserValidAsync(userManager, user, clientId))
                return null;

            var claims = await userManager.GetClaimsAsync(user);
            var marketingConsentClaim = claims.FirstOrDefault((claim) => claim.Type == $"{clientId}:marketing_consent");

            if (await userManager.IsEmailConfirmedAsync(user) && !await userManager.GetTwoFactorEnabledAsync(user))
            {
                await mfaService.EnableMFAAsync(user, MFAMethods.Email);
                user = await userManager.FindByIdAsync(userId);
                ArgumentNullException.ThrowIfNull(user);
            }
            ArgumentNullException.ThrowIfNull(user.Email);

            return new UserModel
            {
                UserId = user.Id.ToString(),
                Email = user.Email,
                IsEmailConfirmed = user.EmailConfirmed,
                MarketingConsent = marketingConsentClaim == null,
                MFA = new MFAConfig
                {
                    IsEnabled = user.TwoFactorEnabled,
                    PrimaryMethod = mfaService.GetPrimaryMethod(user),
                    SecondaryMethod = mfaService.GetSecondaryMethod(user),
                    RemainingValidCodes = await mfaService.GetRemainingValidCodesAsync(user)
                }
            };
        }

        public async Task DeleteUserAsync(string clientId, string userId, string password)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null || !await UserService.IsUserValidAsync(userManager, user, clientId)) return;

            if (!await userManager.CheckPasswordAsync(user, password)) throw new Exception("Wrong password.");

            await userManager.DeleteAsync(user);
        }

        public async Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");

            var result = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);
            return result.Succeeded;
        }

        public async Task<bool> ResetPasswordAsync(string userId, string newPassword)
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");

            var result = await userManager.RemovePasswordAsync(user);
            if (!result.Succeeded) return false;

            await mfaService.ResetMFAAsync(user);
            result = await userManager.AddPasswordAsync(user, newPassword);
            return result.Succeeded;
        }

        public async Task<bool> ClearSessionsAsync(string userId, string clientId, bool all, string jti, string? refreshToken)
        {
            var client = Clients.FindClientById(clientId) ?? throw new Exception("Invalid client_id.");

            var user = await userManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(userManager, user, client.Id)) throw new Exception($"Unable to find user with ID '{user.Id}'.");

            var grants = await persistedGrantStore.GetAllAsync(new PersistedGrantFilter
            {
                ClientId = client.Id,
                SubjectId = user.Id.ToString()
            });
            string? refreshTokenKey = refreshToken != null ? GetHashedKey(refreshToken, IdentityServerConstants.PersistedGrantTypes.RefreshToken) : null;
            List<string> removedKeys = [];
            foreach (var grant in grants)
            {
                if (!all && (grant.Data.Contains(jti) || grant.Key == refreshTokenKey)) continue;
                await persistedGrantStore.RemoveAsync(grant.Key);
                removedKeys.Add(grant.Key);
            }

            await WampServers.NotesnookServer.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(removedKeys));
            await WampServers.MessengerServer.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(removedKeys));
            await WampServers.SubscriptionServer.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(removedKeys));
            // await SendLogoutMessageAsync(user.Id.ToString(), "Session revoked.");
            return true;
        }

        private static string GetHashedKey(string value, string grantType)
        {
            return (value + ":" + grantType).Sha256();
        }
    }
}