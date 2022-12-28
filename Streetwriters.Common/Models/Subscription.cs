using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Data.Attributes;

namespace Streetwriters.Common.Models
{
    [BsonCollection("subscriptions", "subscriptions")]
    public class Subscription : ISubscription
    {
        public Subscription()
        {
            Id = ObjectId.GenerateNewId().ToString();
        }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonIgnore]
        public string OrderId { get; set; }
        [JsonIgnore]
        public string SubscriptionId { get; set; }

        [BsonRepresentation(BsonType.Int32)]
        [JsonPropertyName("appId")]
        public ApplicationType AppId { get; set; }

        [JsonPropertyName("start")]
        public long StartDate { get; set; }

        [JsonPropertyName("expiry")]
        public long ExpiryDate { get; set; }

        [BsonRepresentation(BsonType.Int32)]
        [JsonPropertyName("provider")]
        public SubscriptionProvider Provider { get; set; }

        [BsonRepresentation(BsonType.Int32)]
        [JsonPropertyName("type")]
        public SubscriptionType Type { get; set; }

        [JsonPropertyName("cancelURL")]
        public string CancelURL { get; set; }

        [JsonPropertyName("updateURL")]
        public string UpdateURL { get; set; }

        [JsonPropertyName("productId")]
        public string ProductId { get; set; }

        [JsonIgnore]
        public int TrialExtensionCount { get; set; }
    }
}
