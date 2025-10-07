using System.Net.Http;
using System.Text.Json.Serialization;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Notesnook.API.Models.Responses
{
    public class UserResponse : UserModel, IResponse
    {
        [JsonPropertyName("salt")]
        public string? Salt { get; set; }

        [JsonPropertyName("attachmentsKey")]
        public EncryptedData? AttachmentsKey { get; set; }

        [JsonPropertyName("monographPasswordsKey")]
        public EncryptedData? MonographPasswordsKey { get; set; }

        [JsonPropertyName("inboxKeys")]
        public InboxKeys? InboxKeys { get; set; }

        [JsonPropertyName("subscription")]
        public Subscription? Subscription { get; set; }

        [JsonPropertyName("storageUsed")]
        public long StorageUsed { get; set; }

        [JsonPropertyName("totalStorage")]
        public long TotalStorage { get; set; }

        [JsonIgnore]
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        [JsonIgnore]
        public HttpContent? Content { get; set; }
    }
}
