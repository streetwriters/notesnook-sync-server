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

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Notesnook.API.Models
{
    [MessagePack.MessagePackObject]
    public class EncryptedData : IEncrypted
    {
        [MessagePack.Key("iv")]
        [JsonPropertyName("iv")]
        [BsonElement("iv")]
        [DataMember(Name = "iv")]
        public string IV
        {
            get; set;
        }

        [MessagePack.Key("cipher")]
        [JsonPropertyName("cipher")]
        [BsonElement("cipher")]
        [DataMember(Name = "cipher")]
        public string Cipher
        {
            get; set;
        }

        [MessagePack.Key("length")]
        [JsonPropertyName("length")]
        [BsonElement("length")]
        [DataMember(Name = "length")]
        public long Length { get; set; }

        [MessagePack.Key("salt")]
        [JsonPropertyName("salt")]
        [BsonElement("salt")]
        [DataMember(Name = "salt")]
        public string Salt { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is EncryptedData encryptedData)
            {
                return IV == encryptedData.IV && Salt == encryptedData.Salt && Cipher == encryptedData.Cipher && Length == encryptedData.Length;
            }
            return base.Equals(obj);
        }

        public bool IsEmpty()
        {
            return this.Cipher == null && this.IV == null && this.Length == 0 && this.Salt == null;
        }
    }
}
