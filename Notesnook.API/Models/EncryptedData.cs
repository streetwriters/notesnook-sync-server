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

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Notesnook.API.Models
{
    public class EncryptedData : IEncrypted
    {
        [JsonPropertyName("iv")]
        [BsonElement("iv")]
        [DataMember(Name = "iv")]
        public string IV
        {
            get; set;
        }

        [JsonPropertyName("cipher")]
        [BsonElement("cipher")]
        [DataMember(Name = "cipher")]
        public string Cipher
        {
            get; set;
        }

        [JsonPropertyName("length")]
        [BsonElement("length")]
        [DataMember(Name = "length")]
        public long Length { get; set; }

        [JsonPropertyName("salt")]
        [BsonElement("salt")]
        [DataMember(Name = "salt")]
        public string Salt { get; set; }
    }
}
