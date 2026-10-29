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
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using IdentityModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Models.Responses;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Exceptions;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("users")]
    public class UsersController(IUserService UserService, IUserKeysService UserKeysService, WampServiceAccessor serviceAccessor, ILogger<UsersController> logger) : ControllerBase
    {
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Signup([FromForm] SignupForm form)
        {
            return Ok(await UserService.CreateUserAsync(form));
        }

        [HttpGet]
        public async Task<IActionResult> GetUser()
        {
            var userId = User.GetUserId();
            UserResponse response = await UserService.GetUserAsync(userId);
            if (!response.Success) return BadRequest();
            return Ok(response);
        }

        [Obsolete("Only for old clients. Use POST /users/keys and PUT/DELETE /users/keys/inbox instead.")]
        [HttpPatch]
        public async Task<IActionResult> UpdateUser([FromBody] UserKeys keys)
        {
            var userId = User.GetUserId();
            if (!await UserKeysService.AddUserKeysAsync(userId, keys))
                return Conflict(new { error = "Your encryption keys were changed on another device. Please try again." });
            return Ok(await UserService.GetUserAsync(userId));
        }

        [HttpPost("keys")]
        public async Task<IActionResult> GetOrCreateKeys([FromBody] UserKeys keys, [FromQuery] long? revision)
        {
            var userId = User.GetUserId();
            var result = await UserKeysService.GetOrCreateUserKeysAsync(userId, keys, revision);
            if (result != KeyWriteResult.Success) return KeyWriteRejected(result);
            return Ok(await UserService.GetUserAsync(userId));
        }

        /// <summary>
        /// Commits the pending keys of a password change that failed after the
        /// password was changed. Clients call this when logging in with a
        /// password that unwraps the pending keys, which proves it's the new one.
        /// </summary>
        /// <param name="revision">revision of the user whose pending keys the client checked</param>
        [HttpPost("keys/pending/commit")]
        public async Task<IActionResult> CommitPendingKeys([FromQuery, BindRequired] long revision)
        {
            var userId = User.GetUserId();
            var result = await UserKeysService.CommitPendingKeysAsync(userId, revision, AuthenticatedAt());
            if (result != KeyWriteResult.Success) return KeyWriteRejected(result);
            await EndOtherSessionsAsync(userId);
            return Ok(await UserService.GetUserAsync(userId));
        }

        /// <summary>
        /// Discards the pending keys of a password change that failed before
        /// the password was changed. Clients call this when logging in with a
        /// password that doesn't unwrap the pending keys.
        /// </summary>
        /// <param name="revision">revision of the user whose pending keys the client checked</param>
        [HttpPost("keys/pending/discard")]
        public async Task<IActionResult> DiscardPendingKeys([FromQuery, BindRequired] long revision)
        {
            var userId = User.GetUserId();
            var result = await UserKeysService.DiscardPendingKeysAsync(userId, revision, AuthenticatedAt());
            if (result != KeyWriteResult.Success) return KeyWriteRejected(result);
            return Ok(await UserService.GetUserAsync(userId));
        }

        [HttpPut("keys/inbox")]
        public async Task<IActionResult> SetInboxKeys([FromBody] InboxKeys keys, [FromQuery] long? revision)
        {
            var userId = User.GetUserId();
            if (keys.Public == null || keys.Private == null)
                return BadRequest(new { error = "Both public and private inbox keys are required." });
            var result = await UserKeysService.SetInboxKeysAsync(userId, keys, revision);
            if (result != KeyWriteResult.Success) return KeyWriteRejected(result);
            return Ok(await UserService.GetUserAsync(userId));
        }

        [HttpDelete("keys/inbox")]
        public async Task<IActionResult> RemoveInboxKeys()
        {
            var userId = User.GetUserId();
            if (!await UserKeysService.RemoveInboxKeysAsync(userId))
                return KeyWriteRejected(KeyWriteResult.PasswordChangeInProgress);
            return Ok(await UserService.GetUserAsync(userId));
        }

        [HttpPatch("password/{type}")]
        public async Task<IActionResult> ChangePassword([FromRoute] string type, [FromBody] ChangePasswordForm form)
        {
            var userId = User.GetUserId();
            var isPasswordReset = type == "reset";
            // only tokens from the password reset flow can reset the password
            // without the old one
            if (isPasswordReset && !User.HasClaim(JwtClaimTypes.AuthenticationMethod, Constants.PASSWORD_RESET_AUTHENTICATION_METHOD))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Please use the password reset link sent to your email to reset your password." });
            // keys are staged first and replace the current keys only once
            // the password has changed
            var stagedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var staged = await UserKeysService.StageKeysAsync(userId, form.UserKeys, form.Revision, stagedAt);
            if (staged != KeyWriteResult.Success) return KeyWriteRejected(staged);

            var changed = await TryChangePasswordAsync(userId, form, isPasswordReset);
            // an interrupted change might still have set the password, so
            // we ask Identity instead of assuming
            if (changed == null) changed = await IsPasswordSetAsync(userId, form.NewPassword);
            // if even that is unknown, the pending keys are kept: logging
            // in commits or discards them
            if (changed == null) return BadRequest(new { error = "Your password might have been changed. Please log in again." });
            if (changed == false)
            {
                await UserKeysService.DiscardStagedKeysAsync(userId, stagedAt);
                return BadRequest("Failed to change password.");
            }

            // only fails if the pending keys were resolved by a login
            // while the password was being changed
            var committed = await UserKeysService.CommitStagedKeysAsync(userId, stagedAt);
            if (committed != KeyWriteResult.Success)
            {
                logger.LogError("Password of user with id {UserId} was changed but its pending keys were gone", userId);
                return BadRequest(new { error = "Your password was changed but your encryption keys could not be updated. Please contact support." });
            }

            await EndOtherSessionsAsync(userId);
            return Ok(await UserService.GetUserAsync(userId));
        }


        /// <summary>
        /// Logs out the user's other sessions after their keys were rewrapped.
        /// The keys are already committed, so a failure here mustn't fail the
        /// request: the client has to switch to the new password regardless.
        /// </summary>
        private async Task EndOtherSessionsAsync(string userId)
        {
            var clientId = User.FindFirstValue("client_id");
            var jti = User.FindFirstValue("jti");
            try
            {
                await serviceAccessor.UserAccountService.ClearSessionsAsync(userId, clientId, all: false, jti, null);
                await PublishLogoutAsync(userId, jti, "Password changed.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to end other sessions of user with id: {UserId}", userId);
            }
        }

        private static Task PublishLogoutAsync(string userId, string? jti, string reason) =>
            WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
            {
                UserId = userId,
                OriginTokenId = jti,
                Message = new Message
                {
                    Type = "logout",
                    Data = JsonSerializer.Serialize(new { reason })
                }
            });

        private static async Task<T> RetryAsync<T>(Func<Task<T>> action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
                }
                catch (Exception ex)
                {
                    throw new ServiceUnavailableException("Your account could not be reset. Please try again.", ex);
                }
            }
        }

        /// <returns>null if it's unknown whether the password was changed</returns>
        private async Task<bool?> TryChangePasswordAsync(string userId, ChangePasswordForm form, bool isPasswordReset)
        {
            try
            {
                return isPasswordReset
                    ? await serviceAccessor.UserAccountService.ResetPasswordAsync(userId, form.NewPassword)
                    : await serviceAccessor.UserAccountService.ChangePasswordAsync(userId, form.OldPassword, form.NewPassword);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to change password of user with id: {UserId}", userId);
                return null;
            }
        }

        /// <returns>null if the password couldn't be verified</returns>
        private async Task<bool?> IsPasswordSetAsync(string userId, string password)
        {
            try
            {
                return await serviceAccessor.UserAccountService.VerifyPasswordAsync(userId, password);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to verify password of user with id: {UserId}", userId);
                return null;
            }
        }

        // auth_time is when the session logged in with a password; refreshed tokens keep it
        private long AuthenticatedAt()
        {
            return long.TryParse(User.FindFirstValue(JwtClaimTypes.AuthenticationTime), out var seconds) ? seconds * 1000 : 0;
        }

        private ConflictObjectResult KeyWriteRejected(KeyWriteResult result)
        {
            return result switch
            {
                KeyWriteResult.WrappedWithOldPassword => Conflict(new { error = "password_changed", error_description = "Your password was changed on another device. Please log in again." }),
                KeyWriteResult.KeysChanged => Conflict(new { error = "keys_changed", error_description = "Your account was updated on another device. Please try again." }),
                KeyWriteResult.LoginRequired => Conflict(new { error = "login_required", error_description = "Please log in again to finish changing your password." }),
                _ => Conflict(new { error = "password_change_in_progress", error_description = "Your password is being changed. If this doesn't go away, please log in again." })
            };
        }

        [HttpGet("verifier")]
        public async Task<IActionResult> GetEncryptionVerifier()
        {
            var userId = User.GetUserId();
            var response = await UserService.GetEncryptionVerifier(userId);
            if (response == null) return NotFound();
            return Ok(response);
        }

        [HttpPost("reset")]
        public async Task<IActionResult> Reset([FromForm] bool removeAttachments)
        {
            var userId = this.User.GetUserId();
            var clientId = User.FindFirstValue("client_id");
            var jti = User.FindFirstValue("jti");
            var accounts = serviceAccessor.UserAccountService;

            // the other devices hold the old keys, so they're logged out, and kept from logging back in, before anything is reset
            var lockedOutUntil = await RetryAsync(() => accounts.LockUserAsync(userId));
            try
            {
                await RetryAsync(async () => await accounts.ClearSessionsAsync(userId, clientId, all: false, jti, null) ? true : throw new Exception("Sessions were not cleared."));
                await UserService.ResetUserAsync(userId, removeAttachments);
            }
            finally
            {
                try
                {
                    await accounts.UnlockUserAsync(userId, lockedOutUntil);
                }
                // the lock expires on its own
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to unlock user {UserId} after a reset", userId);
                }
            }

            try
            {
                await PublishLogoutAsync(userId, jti, "Account reset.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to log out the other sessions of user {UserId}", userId);
            }
            return Ok();
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromForm] DeleteAccountForm form)
        {
            var userId = this.User.GetUserId();
            var jti = User.FindFirstValue("jti");
            await UserService.DeleteUserAsync(userId, jti, form.Password);
            return Ok();
        }
    }
}
