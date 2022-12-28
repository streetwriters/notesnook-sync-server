

// Streetwriters.Common.Models.Offer
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Models
{
    public class PromoCode
    {
        [JsonPropertyName("provider")]
        public SubscriptionProvider Provider { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }
    }
}