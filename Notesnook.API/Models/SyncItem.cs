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

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;

namespace Notesnook.API.Models
{
    [MessagePack.MessagePackObject]
    public class SyncItem : ISyncItem
    {
        [IgnoreDataMember]
        [MessagePack.IgnoreMember]
        [JsonPropertyName("dateSynced")]
        public long DateSynced
        {
            get; set;
        }

        [DataMember(Name = "userId")]
        [JsonPropertyName("userId")]
        [MessagePack.Key("userId")]
        public string UserId
        {
            get; set;
        }

        [JsonPropertyName("iv")]
        [DataMember(Name = "iv")]
        [MessagePack.Key("iv")]
        [Required]
        public string IV
        {
            get; set;
        }


        [JsonPropertyName("cipher")]
        [DataMember(Name = "cipher")]
        [MessagePack.Key("cipher")]
        [Required]
        public string Cipher
        {
            get; set;
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
        public ObjectId Id
        {
            get; set;
        }

        [JsonPropertyName("length")]
        [DataMember(Name = "length")]
        [MessagePack.Key("length")]
        [Required]
        public long Length
        {
            get; set;
        }

        [JsonPropertyName("v")]
        [DataMember(Name = "v")]
        [MessagePack.Key("v")]
        [Required]
        public double Version
        {
            get; set;
        }

        [JsonPropertyName("alg")]
        [DataMember(Name = "alg")]
        [MessagePack.Key("alg")]
        [Required]
        public string Algorithm
        {
            get; set;
        } = Algorithms.Default;
    }

    [MessagePack.MessagePackObject]
    public class Attachment : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Content : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Note : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Notebook : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Relation : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Reminder : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Setting : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Shortcut : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Tag : SyncItem { }

    [MessagePack.MessagePackObject]
    public class Color : SyncItem { }
}
