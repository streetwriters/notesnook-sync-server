using System.Threading.Tasks;
using Streetwriters.Common.Models;
using WampSharp.V2.Rpc;

namespace Streetwriters.Common.Interfaces
{
    public interface IUserAccountService
    {
        [WampProcedure("co.streetwriters.identity.users.get_user")]
        Task<UserModel?> GetUserAsync(string clientId, string userId);
        // still called by API instances that predate background deletion; remove once they're all upgraded
        [WampProcedure("co.streetwriters.identity.users.delete_user")]
        Task DeleteUserAsync(string clientId, string userId, string password);
        [WampProcedure("co.streetwriters.identity.users.user_exists")]
        Task<bool> UserExistsAsync(string userId);
        [WampProcedure("co.streetwriters.identity.users.unregister_user")]
        Task UnregisterUserAsync(string clientId, string userId);
        [WampProcedure("co.streetwriters.identity.users.change_password")]
        Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword);
        [WampProcedure("co.streetwriters.identity.users.reset_password")]
        Task<bool> ResetPasswordAsync(string userId, string newPassword);
        [WampProcedure("co.streetwriters.identity.users.verify_password")]
        Task<bool> VerifyPasswordAsync(string userId, string password);
        /// <summary>
        /// Blocks logins until <see cref="UnlockUserAsync"/>, or briefly if that never comes.
        /// Returns the end of a lockout the user already had, for the unlock to restore.
        /// </summary>
        [WampProcedure("co.streetwriters.identity.users.lock_user")]
        Task<long?> LockUserAsync(string userId);
        [WampProcedure("co.streetwriters.identity.users.unlock_user")]
        Task UnlockUserAsync(string userId, long? lockedOutUntil);
        [WampProcedure("co.streetwriters.identity.users.clear_sessions")]
        Task<bool> ClearSessionsAsync(string userId, string clientId, bool all, string jti, string? refreshToken);
        [WampProcedure("co.streetwriters.identity.users.create_user")]
        Task<SignupResponse> CreateUserAsync(string clientId, string email, string password, string? userAgent = null);
    }
}