using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Messages
{
    public class Message
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("data")]
        public string Data { get; set; }
    }
    public class SendSSEMessage
    {
        [JsonPropertyName("sendToAll")]
        public bool SendToAll { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("message")]
        public Message Message { get; set; }

        [JsonPropertyName("originTokenId")]
        public string OriginTokenId { get; set; }
    }
}