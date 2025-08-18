using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Models
{
    public class GiftCard : IDocument
    {
        public GiftCard()
        {
            Id = ObjectId.GenerateNewId().ToString();
        }

        public string Code { get; set; }
        public string OrderId { get; set; }
        public string OrderIdType { get; set; }
        public string ProductId { get; set; }
        public string RedeemedBy { get; set; }
        public long RedeemedAt { get; set; }
        public long Timestamp { get; set; }
        public long Term { get; set; }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonIgnore]
        public string Id { get; set; }
    }
}
