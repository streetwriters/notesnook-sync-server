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