using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AspNetCore.Identity.Mongo.Model;
using IdentityServer4;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Common.Services;
using Streetwriters.Identity.Enums;
using Streetwriters.Identity.Extensions;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Services
{
    public class UserAccountService(UserManager<User> userManager, IMFAService mfaService, IPersistedGrantStore persistedGrantStore, RoleManager<MongoRole> roleManager, EmailAddressValidator emailValidator, ITemplatedEmailSender emailSender, ITokenGenerationService tokenGenerationService, ILogger<UserAccountService> logger) : IUserAccountService
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

        public async Task<SignupResponse> CreateUserAsync(string clientId, string email, string password, string? userAgent = null)
        {
            if (Constants.DISABLE_SIGNUPS)
                return new SignupResponse
                {
                    Errors = ["Creating new accounts is not allowed."]
                };

            try
            {
                var client = Clients.FindClientById(clientId);
                if (client == null) return new SignupResponse
                {
                    Errors = ["Invalid client id."]
                };

                if (await roleManager.FindByNameAsync(clientId) == null)
                    await roleManager.CreateAsync(new MongoRole(clientId));

                // email addresses must be case-insensitive
                email = email.ToLowerInvariant();

                if (!await emailValidator.IsEmailAddressValidAsync(email))
                    return new SignupResponse
                    {
                        Errors = ["Invalid email address."]
                    };

                var result = await userManager.CreateAsync(new User
                {
                    Email = email,
                    EmailConfirmed = Constants.IS_SELF_HOSTED,
                    UserName = email,
                }, password);

                if (result.Succeeded)
                {
                    var user = await userManager.FindByEmailAsync(email);
                    if (user == null) return SignupResponse.Error(["User not found after creation."]);

                    await userManager.AddToRoleAsync(user, client.Id);
                    if (Constants.IS_SELF_HOSTED)
                    {
                        await userManager.AddClaimAsync(user, new Claim(UserService.GetClaimKey(client.Id), "believer"));
                    }
                    else
                    {
                        if (userAgent != null) await userManager.AddClaimAsync(user, new Claim("platform", PlatformFromUserAgent(userAgent)));
                        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                        var callbackUrl = UrlExtensions.TokenLink(user.Id.ToString(), code, client.Id, TokenType.CONFRIM_EMAIL);
                        if (!string.IsNullOrEmpty(user.Email) && callbackUrl != null)
                        {
                            await emailSender.SendConfirmationEmailAsync(user.Email, callbackUrl, client);
                        }
                    }

                    var response = await tokenGenerationService.CreateUserTokensAsync(user, client.Id, 3600);
                    if (response == null) return SignupResponse.Error(["Failed  to generate access token."]);

                    return new SignupResponse
                    {
                        AccessToken = response.AccessToken,
                        AccessTokenLifetime = response.AccessTokenLifetime,
                        RefreshToken = response.RefreshToken,
                        Scope = response.Scope,
                        UserId = user.Id.ToString()
                    };
                }

                return SignupResponse.Error(result.Errors.ToErrors());
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Failed to create user account for email: {Email}", email);
                return SignupResponse.Error(["Failed to create an account."]);
            }
        }

        private static string PlatformFromUserAgent(string? userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "unknown";
            return userAgent.Contains("okhttp/") ? "android" : userAgent.Contains("Darwin/") || userAgent.Contains("CFNetwork/") ? "ios" : "web";
        }
        private static string GetHashedKey(string value, string grantType)
        {
            return (value + ":" + grantType).Sha256();
        }
    }
}