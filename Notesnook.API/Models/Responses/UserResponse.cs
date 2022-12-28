using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Notesnook.API.Interfaces;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Notesnook.API.Models.Responses
{
    public class UserResponse : UserModel, IResponse
    {
        [JsonPropertyName("salt")]
        public string Salt { get; set; }

        [JsonPropertyName("attachmentsKey")]
        public EncryptedData AttachmentsKey { get; set; }

        [JsonPropertyName("subscription")]
        public ISubscription Subscription { get; set; }

        [JsonIgnore]
        public bool Success { get; set; }
        public int StatusCode { get; set; }
    }
}
