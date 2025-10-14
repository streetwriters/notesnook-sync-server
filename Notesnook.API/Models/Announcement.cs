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

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Notesnook.API.Models
{
    public class Announcement
    {
        public Announcement()
        {
            this.Id = ObjectId.GenerateNewId().ToString();
        }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonElement("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        [BsonElement("type")]
        public required string Type { get; set; }

        [JsonPropertyName("timestamp")]
        [BsonElement("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("platforms")]
        [BsonElement("platforms")]
        public required string[] Platforms { get; set; }

        [JsonPropertyName("isActive")]
        [BsonElement("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("userTypes")]
        [BsonElement("userTypes")]
        public required string[] UserTypes { get; set; }

        [JsonPropertyName("appVersion")]
        [BsonElement("appVersion")]
        public int AppVersion { get; set; }

        [JsonPropertyName("body")]
        [BsonElement("body")]
        public required BodyComponent[] Body { get; set; }

        [JsonIgnore]
        [BsonElement("userIds")]
        public string[]? UserIds { get; set; }


        [Obsolete]
        [JsonPropertyName("title")]
        [DataMember(Name = "title")]
        [BsonElement("title")]
        public string? Title { get; set; }

        [Obsolete]
        [JsonPropertyName("description")]
        [BsonElement("description")]
        public string? Description { get; set; }

        [Obsolete]
        [JsonPropertyName("callToActions")]
        [BsonElement("callToActions")]
        public CallToAction[]? CallToActions { get; set; }
    }

    public class BodyComponent
    {
        [JsonPropertyName("type")]
        [BsonElement("type")]
        public required string Type { get; set; }

        [JsonPropertyName("platforms")]
        [BsonElement("platforms")]
        public string[]? Platforms { get; set; }

        [JsonPropertyName("style")]
        [BsonElement("style")]
        public Style? Style { get; set; }

        [JsonPropertyName("src")]
        [BsonElement("src")]
        public string? Src { get; set; }

        [JsonPropertyName("text")]
        [BsonElement("text")]
        public string? Text { get; set; }

        [JsonPropertyName("value")]
        [BsonElement("value")]
        public string? Value { get; set; }

        [JsonPropertyName("items")]
        [BsonElement("items")]
        public BodyComponent[]? Items { get; set; }

        [JsonPropertyName("actions")]
        [BsonElement("actions")]
        public required CallToAction[] Actions { get; set; }
    }

    public class Style
    {
        [JsonPropertyName("marginTop")]
        [BsonElement("marginTop")]
        public int MarginTop { get; set; }

        [JsonPropertyName("marginBottom")]
        [BsonElement("marginBottom")]
        public int MarginBottom { get; set; }

        [JsonPropertyName("textAlign")]
        [BsonElement("textAlign")]
        public string? TextAlign { get; set; }
    }

    public class CallToAction
    {
        [JsonPropertyName("type")]
        [BsonElement("type")]
        public required string Type { get; set; }

        [JsonPropertyName("platforms")]
        [BsonElement("platforms")]
        public string[]? Platforms { get; set; }

        [JsonPropertyName("data")]
        [BsonElement("data")]
        public string? Data { get; set; }

        [JsonPropertyName("title")]
        [BsonElement("title")]
        public string? Title { get; set; }
    }
}