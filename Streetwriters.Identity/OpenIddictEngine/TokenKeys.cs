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
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using Streetwriters.Common;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// The keys tokens are signed and encrypted with, kept in the database so every instance and every
    /// restart uses the same ones. Stored tokens are encrypted: losing the encryption key would end every session.
    /// </summary>
    /// <remarks>
    /// The keys are themselves encrypted, with a key derived from NOTESNOOK_API_SECRET, so the database and its
    /// backups don't hold them: the signing key would let anyone mint the tokens of the sign in steps. When
    /// NOTESNOOK_API_SECRET changes, NOTESNOOK_API_SECRET_PREVIOUS must hold the old one for a start.
    /// </remarks>
    public static class TokenKeys
    {
        private const string CollectionName = "openiddict.keys";
        private const string Scheme = "aes-256-gcm+hkdf-sha256";

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
            var secret = Constants.NOTESNOOK_API_SECRET;
            var document = keys.Find(new BsonDocument("_id", use)).FirstOrDefault();
            if (document == null)
            {
                var id = Guid.NewGuid().ToString("N");
                document = Seal(use, id, create(), secret);
                document["created_at"] = DateTime.UtcNow;
                try { keys.InsertOne(document); }
                // another instance created it first
                catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
                {
                    document = keys.Find(new BsonDocument("_id", use)).First();
                }
            }

            var kid = document["kid"].AsString;
            // as the first builds stored them
            if (document.TryGetValue("material", out var plain))
                return Reseal(keys, document, kid, plain.AsByteArray, secret);

            var material = Open(document, use, kid, secret);
            if (material != null) return (kid, material);

            var previous = Constants.ReadSecret("NOTESNOOK_API_SECRET_PREVIOUS");
            material = previous != null ? Open(document, use, kid, previous) : null;
            if (material != null) return Reseal(keys, document, kid, material, secret);

            // making new keys would end every session
            throw new InvalidOperationException($"The token {use} key can't be decrypted with NOTESNOOK_API_SECRET. If it was changed, start once with the old one in NOTESNOOK_API_SECRET_PREVIOUS.");
        }

        private static (string Id, byte[] Material) Reseal(IMongoCollection<BsonDocument> keys, BsonDocument document, string kid, byte[] material, string secret)
        {
            var use = document["_id"].AsString;
            var sealed_ = Seal(use, kid, material, secret);
            sealed_["created_at"] = document.GetValue("created_at", DateTime.UtcNow);
            keys.ReplaceOne(new BsonDocument("_id", use), sealed_);
            return (kid, material);
        }

        private static BsonDocument Seal(string use, string kid, byte[] material, string secret)
        {
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var ciphertext = new byte[material.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];
            using var aes = new AesGcm(KeyFrom(secret), tag.Length);
            aes.Encrypt(nonce, material, ciphertext, tag, Encoding.UTF8.GetBytes(use + ":" + kid));
            return new BsonDocument
            {
                ["_id"] = use,
                ["kid"] = kid,
                ["scheme"] = Scheme,
                ["nonce"] = new BsonBinaryData(nonce),
                ["ciphertext"] = new BsonBinaryData(ciphertext),
                ["tag"] = new BsonBinaryData(tag),
            };
        }

        private static byte[]? Open(BsonDocument document, string use, string kid, string secret)
        {
            if (document.GetValue("scheme", BsonNull.Value) != Scheme) return null;
            var ciphertext = document["ciphertext"].AsByteArray;
            var material = new byte[ciphertext.Length];
            var tag = document["tag"].AsByteArray;
            try
            {
                using var aes = new AesGcm(KeyFrom(secret), tag.Length);
                aes.Decrypt(document["nonce"].AsByteArray, ciphertext, tag, material, Encoding.UTF8.GetBytes(use + ":" + kid));
                return material;
            }
            catch (AuthenticationTagMismatchException)
            {
                return null;
            }
        }

        private static byte[] KeyFrom(string secret) =>
            HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, info: Encoding.UTF8.GetBytes("streetwriters.identity.openiddict.keys"));
    }
}
