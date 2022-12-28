using System.Threading;
using System.Threading.Tasks;
using Notesnook.API.Models.Responses;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Interfaces
{
    public interface IUserService
    {
        Task CreateUserAsync();
        Task<bool> DeleteUserAsync(string userId, string jti);
        Task<bool> ResetUserAsync(string userId, bool removeAttachments);
        Task<UserResponse> GetUserAsync(bool repair = true);
        Task SetUserAttachmentsKeyAsync(string userId, IEncrypted key);
    }
}