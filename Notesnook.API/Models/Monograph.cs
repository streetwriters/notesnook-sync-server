using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;
using Streetwriters.Data.Attributes;

namespace Notesnook.API.Models
{
    [BsonCollection("notesnook", "monographs")]
    public class Monograph : IMonograph
    {
        public Monograph()
        {
            Id = ObjectId.GenerateNewId().ToString();
        }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("selfDestruct")]
        public bool SelfDestruct { get; set; }

        [JsonPropertyName("encryptedContent")]
        public EncryptedData EncryptedContent { get; set; }

        [JsonPropertyName("datePublished")]
        public long DatePublished { get; set; }

        [JsonPropertyName("content")]
        [BsonIgnore]
        public string Content { get; set; }

        [JsonIgnore]
        public byte[] CompressedContent { get; set; }
    }
}