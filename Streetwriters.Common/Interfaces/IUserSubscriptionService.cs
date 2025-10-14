using System.Threading.Tasks;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Models;
using WampSharp.V2.Rpc;

namespace Streetwriters.Common.Interfaces
{
    public interface IUserSubscriptionService
    {
        [WampProcedure("co.streetwriters.subscriptions.subscriptions.get_user_subscription")]
        Task<Subscription?> GetUserSubscriptionAsync(string clientId, string userId);
        Subscription TransformUserSubscription(Subscription subscription);
    }
}