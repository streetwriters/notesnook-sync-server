using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Messages
{
    public class DeleteUserMessage
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }
    }
}