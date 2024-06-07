using System.Threading.Tasks;
using Streetwriters.Common.Models;
using WampSharp.V2.Rpc;

namespace Streetwriters.Common.Interfaces
{
    public interface IUserAccountService
    {
        [WampProcedure("co.streetwriters.identity.users.get_user")]
        Task<UserModel> GetUserAsync(string clientId, string userId);
        [WampProcedure("co.streetwriters.identity.users.delete_user")]
        Task DeleteUserAsync(string clientId, string userId, string password);
        // [WampProcedure("co.streetwriters.identity.users.create_user")]
        // Task<UserModel> CreateUserAsync();
    }
}