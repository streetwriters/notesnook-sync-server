using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Notesnook.API.Models
{
    public class EncryptedData : IEncrypted
    {
        [JsonPropertyName("iv")]
        [BsonElement("iv")]
        [DataMember(Name = "iv")]
        public string IV
        {
            get; set;
        }

        [JsonPropertyName("cipher")]
        [BsonElement("cipher")]
        [DataMember(Name = "cipher")]
        public string Cipher
        {
            get; set;
        }

        [JsonPropertyName("length")]
        [BsonElement("length")]
        [DataMember(Name = "length")]
        public long Length { get; set; }

        [JsonPropertyName("salt")]
        [BsonElement("salt")]
        [DataMember(Name = "salt")]
        public string Salt { get; set; }
    }
}
