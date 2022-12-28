/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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
