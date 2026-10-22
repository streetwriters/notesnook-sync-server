using System.Threading.Tasks;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using WampSharp.V2.Rpc;

namespace Streetwriters.Common.Interfaces
{
    public interface IUserSubscriptionService
    {
        [WampProcedure("co.streetwriters.subscriptions.subscriptions.get_user_subscription")]
        Task<Subscription?> GetUserSubscriptionAsync(string clientId, string userId);
        [WampProcedure("co.streetwriters.subscriptions.subscriptions.is_user_subscribed")]
        Task<bool> IsUserSubscribedAsync(string clientId, string userId);
        [WampProcedure("co.streetwriters.subscriptions.subscriptions.create_subscription")]
        Task CreateSubscriptionAsync(CreateSubscriptionMessageV2 message);
        Subscription TransformUserSubscription(Subscription subscription);
    }
}