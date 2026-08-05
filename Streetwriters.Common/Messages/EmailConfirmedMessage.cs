using System.Text.Json.Serialization;
using Streetwriters.Common.Enums;

namespace Streetwriters.Common.Messages
{
    public class EmailConfirmedMessage
    {
        [JsonPropertyName("userId")]
        public required string UserId { get; set; }

        [JsonPropertyName("clientId")]
        public required string ClientId { get; set; }

        [JsonPropertyName("appId")]
        public ApplicationType AppId { get; set; }

        [JsonPropertyName("confirmedAt")]
        public long ConfirmedAt { get; set; }
    }
}