using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Models
{
    public class SubscriptionResponse : Response
    {
        [JsonPropertyName("subscription")]
        public ISubscription Subscription { get; set; }
    }
}
