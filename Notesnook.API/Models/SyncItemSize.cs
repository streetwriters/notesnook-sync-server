using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Notesnook.API.Models
{
    public class SyncItemSize
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public int Size { get; set; }
    }
}
