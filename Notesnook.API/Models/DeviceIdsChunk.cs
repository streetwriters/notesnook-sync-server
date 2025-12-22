using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Notesnook.API.Models
{
    public class DeviceIdsChunk
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public required string UserId { get; set; }
        public required string DeviceId { get; set; }
        public required string Key { get; set; }
        public required string[] Ids { get; set; } = [];
    }
}