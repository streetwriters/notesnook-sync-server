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
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// The keys tokens are signed and encrypted with, kept in the database so every instance and every
    /// restart uses the same ones. Stored tokens are encrypted: losing the encryption key would end every session.
    /// </summary>
    public static class TokenKeys
    {
        private const string CollectionName = "openiddict.keys";

        public static SecurityKey LoadSigningKey(IMongoDatabase database)
        {
            var key = Load(database, "signing", () =>
            {
                using var rsa = RSA.Create(2048);
                return rsa.ExportPkcs8PrivateKey();
            });
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(key.Material, out _);
            return new RsaSecurityKey(rsa) { KeyId = key.Id };
        }

        public static SecurityKey LoadEncryptionKey(IMongoDatabase database)
        {
            var key = Load(database, "encryption", () => RandomNumberGenerator.GetBytes(32));
            return new SymmetricSecurityKey(key.Material) { KeyId = key.Id };
        }

        private static (string Id, byte[] Material) Load(IMongoDatabase database, string use, Func<byte[]> create)
        {
            var keys = database.GetCollection<BsonDocument>(CollectionName);
            var document = keys.Find(new BsonDocument("_id", use)).FirstOrDefault();
            if (document == null)
            {
                document = new BsonDocument
                {
                    ["_id"] = use,
                    ["kid"] = Guid.NewGuid().ToString("N"),
                    ["material"] = new BsonBinaryData(create()),
                    ["created_at"] = DateTime.UtcNow,
                };
                try { keys.InsertOne(document); }
                // another instance created it first
                catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
                {
                    document = keys.Find(new BsonDocument("_id", use)).First();
                }
            }
            return (document["kid"].AsString, document["material"].AsByteArray);
        }
    }
}
