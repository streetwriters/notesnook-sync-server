using System.Threading.Tasks;
using Streetwriters.Common.Models;
using WampSharp.V2.Rpc;

namespace Streetwriters.Common.Interfaces
{
    public interface IUserAccountService
    {
        [WampProcedure("co.streetwriters.identity.users.get_user")]
        Task<UserModel?> GetUserAsync(string clientId, string userId);
        [WampProcedure("co.streetwriters.identity.users.delete_user")]
        Task DeleteUserAsync(string clientId, string userId, string password);
        [WampProcedure("co.streetwriters.identity.users.change_password")]
        Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword);
        [WampProcedure("co.streetwriters.identity.users.reset_password")]
        Task<bool> ResetPasswordAsync(string userId, string newPassword);
        [WampProcedure("co.streetwriters.identity.users.clear_sessions")]
        Task<bool> ClearSessionsAsync(string userId, string clientId, bool all, string jti, string? refreshToken);
        [WampProcedure("co.streetwriters.identity.users.create_user")]
        Task<SignupResponse> CreateUserAsync(string clientId, string email, string password, string? userAgent = null);
    }
}