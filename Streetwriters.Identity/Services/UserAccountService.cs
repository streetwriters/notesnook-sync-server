using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AspNetCore.Identity.Mongo.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
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
using WampSharp.V2.Core.Contracts;

namespace Streetwriters.Identity.Services
{
    public class UserAccountService(UserManager<User> userManager, IMFAService mfaService, ISessionStore sessionStore, RoleManager<MongoRole> roleManager, EmailAddressValidator emailValidator, ITemplatedEmailSender emailSender, ITokenIssuer tokenIssuer, ILogger<UserAccountService> logger) : IUserAccountService
    {
        // long enough for the API to finish an account reset, short enough to not lock out a user it never unlocks
        private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ClearCacheTimeout = TimeSpan.FromSeconds(10);

        public async Task<UserModel?> GetUserAsync(string clientId, string userId)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null || !await UserService.IsUserValidAsync(userManager, user, clientId))
                return null;

            var claims = await userManager.GetClaimsAsync(user);
            var marketingConsentClaim = claims.FirstOrDefault((claim) => claim.Type == $"{clientId}:marketing_consent");

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
            await sessionStore.EndAllSessionsAsync(userId);
        }

        // a malformed id makes FindByIdAsync throw, and can never belong to a user
        public async Task<bool> UserExistsAsync(string userId) => ObjectId.TryParse(userId, out _) && await userManager.FindByIdAsync(userId) != null;

        public async Task UnregisterUserAsync(string clientId, string userId)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null) return;
            if (!await UserService.IsUserValidAsync(userManager, user, clientId)) throw new Exception($"Unable to find user with ID '{user.Id}'.");

            // sessions end first, so a failure leaves a signed-out user who can retry rather than a deleted one still signed in
            var removedKeys = await sessionStore.EndAllSessionsAsync(userId);
            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded) throw new Exception($"Failed to delete user {userId}: {string.Join(", ", result.Errors.Select((e) => e.Description))}");

            // the user is already deleted, and cached tokens expire before UserPurgeJob runs
            try
            {
                await ClearCacheAsync(removedKeys);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clear the token cache of unregistered user {UserId}", userId);
            }
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

            // a single write, so a failed reset leaves the old password set
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, resetToken, newPassword);
            if (!result.Succeeded) return false;

            if (await userManager.GetTwoFactorEnabledAsync(user))
                await mfaService.ResetMFAAsync(user);

            // force change email to lowercase if it is not already
            if (user.Email != null && user.Email != user.Email.ToLower())
            {
                var token = await userManager.GenerateChangeEmailTokenAsync(user, user.Email.ToLower());
                // the password is already reset so this mustn't fail the reset
                var emailResult = await userManager.ChangeEmailAsync(user, user.Email.ToLower(), token);
                if (!emailResult.Succeeded)
                    logger.LogWarning("Failed to lowercase email of user {UserId}: {Errors}", userId, string.Join(", ", emailResult.Errors.Select((e) => e.Description)));
            }
            return true;
        }

        public async Task<bool> VerifyPasswordAsync(string userId, string password)
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");
            return await userManager.CheckPasswordAsync(user, password);
        }

        public async Task<bool> ClearSessionsAsync(string userId, string clientId, bool all, string jti, string? refreshToken)
        {
            var client = Clients.FindClientById(clientId) ?? throw new Exception("Invalid client_id.");

            var user = await userManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");
            if (!await UserService.IsUserValidAsync(userManager, user, client.Id)) throw new Exception($"Unable to find user with ID '{user.Id}'.");

            var removedKeys = all
                ? await sessionStore.EndSessionsAsync(client.Id, user.Id.ToString(), null, null)
                : await sessionStore.EndSessionsAsync(client.Id, user.Id.ToString(), jti, refreshToken);

            await ClearCacheAsync(removedKeys);
            // await SendLogoutMessageAsync(user.Id.ToString(), "Session revoked.");
            return true;
        }

        public async Task<long?> LockUserAsync(string userId)
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");
            var lockedOutUntil = user.LockoutEnd > DateTimeOffset.UtcNow ? user.LockoutEnd : null;
            var until = DateTimeOffset.UtcNow.Add(LockDuration);

            // users created before lockout was enabled have it off
            EnsureSucceeded(await userManager.SetLockoutEnabledAsync(user, true), userId);
            user.ResetLockoutEnd = until.ToUnixTimeMilliseconds();
            EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, lockedOutUntil > until ? lockedOutUntil : until), userId);
            return lockedOutUntil?.ToUnixTimeMilliseconds();
        }

        public async Task UnlockUserAsync(string userId, long? lockedOutUntil)
        {
            var user = await userManager.FindByIdAsync(userId) ?? throw new Exception("User not found.");
            EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, lockedOutUntil == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(lockedOutUntil.Value)), userId);
        }

        private static void EnsureSucceeded(IdentityResult result, string userId)
        {
            if (!result.Succeeded) throw new Exception($"Failed to update the lockout of user {userId}: {string.Join(", ", result.Errors.Select((e) => e.Description))}");
        }

        // throws unless the API and the subscription server confirmed it, since a token they still have cached keeps working there
        private async Task ClearCacheAsync(List<string> keys)
        {
            await ClearCacheAsync(WampServers.NotesnookServer, keys);
            if (!Constants.IS_SELF_HOSTED)
                await ClearCacheAsync(WampServers.SubscriptionServer, keys);

            // it only streams events, and can't change user data
            try
            {
                await WampServers.MessengerServer.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(keys));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clear the token cache of the messenger");
            }
        }

        private static async Task ClearCacheAsync(WampServer server, List<string> keys)
        {
            try
            {
                var cache = await server.GetServiceAsync<ITokenCacheService>().WaitAsync(ClearCacheTimeout);
                await cache.ClearAsync([.. keys]).WaitAsync(ClearCacheTimeout);
            }
            // servers that predate the token cache call only listen for the message; remove once they're all upgraded
            catch (WampException ex) when (ex.ErrorUri == WampErrors.NoSuchProcedure)
            {
                await server.PublishMessageAsync(IdentityServerTopics.ClearCacheTopic, new ClearCacheMessage(keys));
            }
        }

        public async Task<SignupResponse> CreateUserAsync(string clientId, string email, string password, string? userAgent = null)
        {
            if (Constants.DISABLE_SIGNUPS)
                return new SignupResponse
                {
                    Errors = ["Creating new accounts is not allowed."]
                };

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

            IdentityResult result;
            try
            {
                result = await userManager.CreateAsync(new User
                {
                    Email = email,
                    EmailConfirmed = Constants.IS_SELF_HOSTED,
                    UserName = email,
                }, password);
            }
            // concurrent signups can all pass Identity's duplicate check; the unique index rejects all but one
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                result = IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(email));
            }

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
                    await mfaService.EnableMFAAsync(user, MFAMethods.Email);
                    if (userAgent != null) await userManager.AddClaimAsync(user, new Claim("platform", PlatformFromUserAgent(userAgent)));
                    var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                    var callbackUrl = UrlExtensions.TokenLink(user.Id.ToString(), code, client.Id, TokenType.CONFRIM_EMAIL);
                    if (!string.IsNullOrEmpty(user.Email) && callbackUrl != null)
                    {
                        await emailSender.SendConfirmationEmailAsync(user.Email, callbackUrl, client);
                    }
                }

                var response = await tokenIssuer.IssueUserTokensAsync(user, client.Id, 3600);
                if (response == null) return SignupResponse.Error(["Failed  to generate access token."]);

                return new SignupResponse
                {
                    AccessToken = response.AccessToken,
                    AccessTokenLifetime = response.ExpiresIn,
                    RefreshToken = response.RefreshToken,
                    Scope = response.Scope,
                    UserId = user.Id.ToString()
                };
            }

            var otherErrors = result.Errors
                .Where(e => e.Code != "DuplicateUserName" && e.Code != "DuplicateEmail")
                .ToErrors();
            var hasDuplicate = result.Errors.Any(e => e.Code == "DuplicateUserName" || e.Code == "DuplicateEmail");
            var errors = hasDuplicate
                ? ["Unable to create an account on this email.", .. otherErrors]
                : otherErrors;
            return SignupResponse.Error(errors);
        }

        private static string PlatformFromUserAgent(string? userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "unknown";
            return userAgent.Contains("okhttp/") ? "android" : userAgent.Contains("Darwin/") || userAgent.Contains("CFNetwork/") ? "ios" : "web";
        }
    }
}