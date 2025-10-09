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

namespace Notesnook.API.Models
{

    [MessagePack.MessagePackObject]
    public class InboxSyncItem : SyncItem
    {
        [DataMember(Name = "key")]
        [JsonPropertyName("key")]
        [MessagePack.Key("key")]
        [Required]
        public EncryptedKey Key
        {
            get; set;
        }

        [DataMember(Name = "salt")]
        [JsonPropertyName("salt")]
        [MessagePack.Key("salt")]
        [Required]
        public string Salt
        {
            get; set;
        }
    }

    [MessagePack.MessagePackObject]
    public class EncryptedKey
    {
        [DataMember(Name = "alg")]
        [JsonPropertyName("alg")]
        [MessagePack.Key("alg")]
        [Required]
        public string Algorithm
        {
            get; set;
        }

        [DataMember(Name = "cipher")]
        [JsonPropertyName("cipher")]
        [MessagePack.Key("cipher")]
        [Required]
        public string Cipher
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
    }
}