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
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using Notesnook.API.Interfaces;

namespace Notesnook.API.Models
{
    [MessagePack.MessagePackObject]
    public class SyncItem
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
        public string? UserId
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
        public string? ItemId
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
        }
    }

    public class SyncItemBsonSerializer : SerializerBase<SyncItem>
    {
        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, SyncItem value)
        {
            var writer = context.Writer;
            writer.WriteStartDocument();

            if (value.Id != ObjectId.Empty)
            {
                writer.WriteName("_id");
                writer.WriteObjectId(value.Id);
            }

            writer.WriteName("DateSynced");
            writer.WriteInt64(value.DateSynced);

            writer.WriteName("UserId");
            writer.WriteString(value.UserId);

            writer.WriteName("IV");
            writer.WriteString(value.IV);

            writer.WriteName("Cipher");
            writer.WriteString(value.Cipher);

            writer.WriteName("ItemId");
            writer.WriteString(value.ItemId);

            writer.WriteName("Length");
            writer.WriteInt64(value.Length);

            writer.WriteName("Version");
            writer.WriteDouble(value.Version);

            writer.WriteName("Algorithm");
            writer.WriteString(value.Algorithm);

            writer.WriteEndDocument();
        }

        public override SyncItem Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var syncItem = new SyncItem();
            var bsonReader = context.Reader;
            bsonReader.ReadStartDocument();

            while (bsonReader.ReadBsonType() != BsonType.EndOfDocument)
            {
                var fieldName = bsonReader.ReadName();

                switch (fieldName)
                {
                    case "DateSynced":
                        syncItem.DateSynced = bsonReader.ReadInt64();
                        break;
                    case "UserId":
                        syncItem.UserId = bsonReader.ReadString();
                        break;
                    case "IV":
                        syncItem.IV = bsonReader.ReadString();
                        break;
                    case "Cipher":
                        syncItem.Cipher = bsonReader.ReadString();
                        break;
                    case "ItemId":
                        syncItem.ItemId = bsonReader.ReadString();
                        break;
                    case "_id":
                        syncItem.Id = bsonReader.ReadObjectId();
                        break;
                    case "Length":
                        syncItem.Length = bsonReader.ReadInt64();
                        break;
                    case "Version":
                        syncItem.Version = bsonReader.ReadDouble();
                        break;
                    case "Algorithm":
                        syncItem.Algorithm = bsonReader.ReadString();
                        break;
                    default:
                        bsonReader.SkipValue();
                        break;
                }
            }
            bsonReader.ReadEndDocument();
            return syncItem;
        }
    }
}
