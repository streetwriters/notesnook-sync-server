/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Runtime.Serialization;

namespace Notesnook.API.Models
{
    public class ObjectWithId
    {
        [BsonId]
        [BsonIgnoreIfDefault]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id
        {
            get; set;
        }

        public string ItemId
        {
            get; set;
        }
    }

    public class Monograph
    {
        public Monograph()
        {
            Id = ObjectId.GenerateNewId().ToString();
        }

        [DataMember(Name = "id")]
        [JsonPropertyName("id")]
        [MessagePack.Key("id")]
        public string ItemId
        {
            get; set;
        }

        [BsonId]
        [BsonIgnoreIfDefault]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonIgnore]
        [MessagePack.IgnoreMember]
        public string Id
        {
            get; set;
        }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("userId")]
        public string? UserId { get; set; }

        [JsonPropertyName("selfDestruct")]
        public bool SelfDestruct { get; set; }

        [JsonPropertyName("encryptedContent")]
        public EncryptedData? EncryptedContent { get; set; }

        [JsonPropertyName("datePublished")]
        public long DatePublished { get; set; }

        [JsonPropertyName("content")]
        [BsonIgnore]
        public string? Content { get; set; }

        [JsonIgnore]
        public byte[]? CompressedContent { get; set; }

        [JsonPropertyName("password")]
        public EncryptedData? Password { get; set; }

        [JsonPropertyName("deleted")]
        public bool Deleted { get; set; }
    }
}