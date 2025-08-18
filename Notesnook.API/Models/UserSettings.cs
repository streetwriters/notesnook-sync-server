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

namespace Notesnook.API.Models
{
    public class Limit
    {
        public long Value { get; set; }
        public long UpdatedAt { get; set; }
    }

    public class UserSettings : IUserSettings
    {
        public UserSettings()
        {
            this.Id = ObjectId.GenerateNewId().ToString();
        }
        public string UserId { get; set; }
        public long LastSynced { get; set; }
        public string Salt { get; set; }
        public EncryptedData VaultKey { get; set; }
        public EncryptedData AttachmentsKey { get; set; }
        public EncryptedData MonographPasswordsKey { get; set; }
        public Limit StorageLimit { get; set; }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
    }
}
