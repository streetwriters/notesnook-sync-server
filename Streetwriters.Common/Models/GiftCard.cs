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
            Id = ObjectId.GenerateNewId();
        }

        public required string Code { get; set; }
        public required string OrderId { get; set; }
        public required string OrderIdType { get; set; }
        public required string ProductId { get; set; }
        public string? RedeemedBy { get; set; }
        public long RedeemedAt { get; set; }
        public long Timestamp { get; set; }
        public long Term { get; set; }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonIgnore]
        public ObjectId Id { get; set; }
    }
}
