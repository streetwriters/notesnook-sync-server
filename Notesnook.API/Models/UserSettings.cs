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
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Notesnook.API.Interfaces;

namespace Notesnook.API.Models
{
    public class Limit
    {
        private long _value = 0;
        public long Value
        {
            get => _value;
            set
            {
                _value = value;
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        public long UpdatedAt
        {
            get;
            set;
        }
    }

    public class UserSettings
    {
        public UserSettings()
        {
            this.Id = ObjectId.GenerateNewId();
        }
        public required string UserId { get; set; }
        public long LastSynced { get; set; }
        public required string Salt { get; set; }
        public EncryptedData? VaultKey { get; set; }
        public EncryptedData? AttachmentsKey { get; set; }
        public EncryptedData? MonographPasswordsKey { get; set; }
        public EncryptedData? DataEncryptionKey { get; set; }
        public EncryptedData? LegacyDataEncryptionKey { get; set; }
        public InboxKeys? InboxKeys { get; set; }
        public Limit? StorageLimit { get; set; }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }
    }
}
