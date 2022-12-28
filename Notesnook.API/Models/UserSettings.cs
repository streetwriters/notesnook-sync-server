using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;
using Streetwriters.Data.Attributes;

namespace Notesnook.API.Models
{
    [BsonCollection("notesnook", "user_settings")]
    public class UserSettings : IUserSettings
    {
        public UserSettings()
        {
            this.Id = ObjectId.GenerateNewId().ToString();
        }
        public string UserId { get; set; }
        public long LastSynced { get; set; }
        public string Salt { get; set; }
        public EncryptedData VaultKey { get; set; }
        public EncryptedData AttachmentsKey { get; set; }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
    }
}
