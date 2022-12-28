using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;
using Streetwriters.Data.Attributes;

namespace Notesnook.API.Models
{
    public class SyncItem : ISyncItem
    {
        [IgnoreDataMember]
        [JsonPropertyName("dateSynced")]
        public long DateSynced
        {
            get; set;
        }

        [DataMember(Name = "userId")]
        [JsonPropertyName("userId")]
        public string UserId
        {
            get; set;
        }

        [JsonPropertyName("iv")]
        [DataMember(Name = "iv")]
        [Required]
        public string IV
        {
            get; set;
        }


        [JsonPropertyName("cipher")]
        [DataMember(Name = "cipher")]
        [Required]
        public string Cipher
        {
            get; set;
        }

        [DataMember(Name = "id")]
        [JsonPropertyName("id")]
        public string ItemId
        {
            get; set;
        }

        [BsonId]
        [BsonIgnoreIfDefault]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonIgnore]
        public ObjectId Id
        {
            get; set;
        }

        [JsonPropertyName("length")]
        [DataMember(Name = "length")]
        [Required]
        public long Length
        {
            get; set;
        }

        [JsonPropertyName("v")]
        [DataMember(Name = "v")]
        [Required]
        public double Version
        {
            get; set;
        }

        [JsonPropertyName("alg")]
        [DataMember(Name = "alg")]
        [Required]
        public string Algorithm
        {
            get; set;
        } = Algorithms.Default;
    }

    [BsonCollection("notesnook", "attachments")]
    public class Attachment : SyncItem { }

    [BsonCollection("notesnook", "content")]
    public class Content : SyncItem { }

    [BsonCollection("notesnook", "notes")]
    public class Note : SyncItem { }

    [BsonCollection("notesnook", "notebooks")]
    public class Notebook : SyncItem { }

    [BsonCollection("notesnook", "relations")]
    public class Relation : SyncItem { }

    [BsonCollection("notesnook", "reminders")]
    public class Reminder : SyncItem { }

    [BsonCollection("notesnook", "settings")]
    public class Setting : SyncItem { }

    [BsonCollection("notesnook", "shortcuts")]
    public class Shortcut : SyncItem { }
}
