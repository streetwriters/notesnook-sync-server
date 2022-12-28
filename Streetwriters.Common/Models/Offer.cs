

// Streetwriters.Common.Models.Offer
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Data.Attributes;

namespace Streetwriters.Common.Models
{
    [BsonCollection("subscriptions", "offers")]
    public class Offer : IOffer
    {
        public Offer()
        {
            Id = ObjectId.GenerateNewId().ToString();
        }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("appId")]
        public ApplicationType AppId { get; set; }

        [JsonPropertyName("promoCode")]
        public string PromoCode { get; set; }

        [JsonPropertyName("codes")]
        public PromoCode[] Codes { get; set; }
    }
}