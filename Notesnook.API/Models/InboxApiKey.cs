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
using NanoidDotNet;

namespace Notesnook.API.Models
{
    public class InboxApiKey
    {
        public InboxApiKey()
        {
            var random = Nanoid.Generate(size: 64);
            Key = "nn__" + random;
        }

        [BsonId]
        [BsonIgnoreIfDefault]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonIgnore]
        [MessagePack.IgnoreMember]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("userId")]
        public required string UserId { get; set; }

        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("dateCreated")]
        public long DateCreated { get; set; }

        [JsonPropertyName("expiryDate")]
        public long ExpiryDate { get; set; }

        [JsonPropertyName("lastUsedAt")]
        public long LastUsedAt { get; set; }
    }
}
