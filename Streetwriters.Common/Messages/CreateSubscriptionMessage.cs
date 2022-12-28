using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Messages
{
    public class CreateSubscriptionMessage
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("provider")]
        public SubscriptionProvider Provider { get; set; }

        [JsonPropertyName("appId")]
        public ApplicationType AppId { get; set; }

        [JsonPropertyName("type")]
        public SubscriptionType Type { get; set; }

        [JsonPropertyName("start")]
        public long StartTime { get; set; }

        [JsonPropertyName("expiry")]
        public long ExpiryTime { get; set; }

        [JsonPropertyName("orderId")]
        public string OrderId { get; set; }

        [JsonPropertyName("updateURL")]
        public string UpdateURL { get; set; }

        [JsonPropertyName("cancelURL")]
        public string CancelURL { get; set; }

        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonPropertyName("productId")]
        public string ProductId { get; set; }
        public bool Extend { get; set; }
    }
}