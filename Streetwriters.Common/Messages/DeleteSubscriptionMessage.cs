using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Messages
{
    public class DeleteSubscriptionMessage
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("appId")]
        public ApplicationType AppId { get; set; }
    }
}