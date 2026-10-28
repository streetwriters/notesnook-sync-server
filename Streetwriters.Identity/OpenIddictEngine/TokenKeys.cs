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
using System.Collections.Generic;
using System.Linq;
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
    /// <para>
    /// The keys are themselves encrypted, with a key derived from NOTESNOOK_API_SECRET, so the database and its
    /// backups don't hold them: the signing key would let anyone mint the tokens of the sign in steps. When
    /// NOTESNOOK_API_SECRET changes, NOTESNOOK_API_SECRET_PREVIOUS must hold the old one for a start.
    /// </para>
    /// <para>
    /// If a key may have leaked, the keys command replaces both (see TokenKeysCommand): the next
    /// start uses only the new ones, which ends every session. Each replacement is a new generation (signing:2,
    /// encryption:2, ...); the old ones are kept, marked retired, and never loaded again.
    /// </para>
    /// </remarks>
    public static class TokenKeys
    {
        private const string CollectionName = "openiddict.keys";
        private const string Scheme = "aes-256-gcm+hkdf-sha256";

        public const string Signing = "signing";
        public const string Encryption = "encryption";

        public static SecurityKey LoadSigningKey(IMongoDatabase database)
        {
            var key = Load(database, Signing);
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(key.Material, out _);
            return new RsaSecurityKey(rsa) { KeyId = key.Id };
        }

        public static SecurityKey LoadEncryptionKey(IMongoDatabase database)
        {
            var key = Load(database, Encryption);
            return new SymmetricSecurityKey(key.Material) { KeyId = key.Id };
        }

        /// <summary>Makes new keys and retires every other one, so the next start uses only the new ones.</summary>
        /// <returns>The new keys' ids.</returns>
        public static (string Signing, string Encryption) Replace(IMongoDatabase database, DateTime now)
        {
            var keys = database.GetCollection<BsonDocument>(CollectionName);
            var replacements = new[] { Signing, Encryption }.Select((use) => (use, Create(keys, use, now))).ToList();
            keys.UpdateMany(
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Nin("kid", replacements.Select((r) => r.Item2)),
                    Builders<BsonDocument>.Filter.Exists("retired_at", false)),
                Builders<BsonDocument>.Update.Set("retired_at", now));
            return (replacements[0].Item2, replacements[1].Item2);
        }

        /// <returns>Every key, newest first, without its material.</returns>
        public static List<(string Use, int Generation, string Kid, DateTime CreatedAt, DateTime? RetiredAt)> List(IMongoDatabase database)
        {
            var keys = database.GetCollection<BsonDocument>(CollectionName);
            return new[] { Signing, Encryption }.SelectMany((use) => Find(keys, use).Select((g) => (use, g.Number, g.Document["kid"].AsString,
                CreatedAt(g.Document), g.Document.TryGetValue("retired_at", out var retired) ? retired.ToUniversalTime() : (DateTime?)null))).ToList();
        }

        private static (string Id, byte[] Material) Load(IMongoDatabase database, string use)
        {
            var keys = database.GetCollection<BsonDocument>(CollectionName);
            var generations = Find(keys, use);
            if (generations.Count == 0)
            {
                Create(keys, use, DateTime.UtcNow);
                generations = Find(keys, use);
            }
            // replacing makes a new generation before it retires the others, so the newest is the one to use
            var current = generations.FirstOrDefault(IsActive).Document
                // making new keys would end every session
                ?? throw new InvalidOperationException($"Every token {use} key is retired.");
            return Open(keys, use, current, Constants.NOTESNOOK_API_SECRET);
        }

        private static bool IsActive((int Number, BsonDocument Document) generation) => !generation.Document.Contains("retired_at");

        /// <returns>The generations of the key, newest first.</returns>
        private static List<(int Number, BsonDocument Document)> Find(IMongoCollection<BsonDocument> keys, string use)
        {
            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", use),
                Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression("^" + use + ":[0-9]+$")));
            return keys.Find(filter).ToList()
                .Select((d) => (Number: d["_id"].AsString == use ? 1 : int.Parse(d["_id"].AsString[(use.Length + 1)..]), Document: d))
                .OrderByDescending((g) => g.Number)
                .ToList();
        }

        /// <returns>The new key's id.</returns>
        private static string Create(IMongoCollection<BsonDocument> keys, string use, DateTime now)
        {
            var generation = (Find(keys, use).FirstOrDefault().Number) + 1;
            byte[] material;
            if (use == Signing)
            {
                using var rsa = RSA.Create(2048);
                material = rsa.ExportPkcs8PrivateKey();
            }
            else material = RandomNumberGenerator.GetBytes(32);

            var kid = Guid.NewGuid().ToString("N");
            var document = Seal(use, kid, material, Constants.NOTESNOOK_API_SECRET);
            // the first generation is stored as "signing" or "encryption", as before keys could be replaced
            document["_id"] = generation == 1 ? use : use + ":" + generation;
            document["created_at"] = now;
            try { keys.InsertOne(document); }
            // another instance made it first
            catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                return keys.Find(new BsonDocument("_id", document["_id"])).First()["kid"].AsString;
            }
            return kid;
        }

        private static DateTime CreatedAt(BsonDocument document) =>
            document.TryGetValue("created_at", out var created) ? created.ToUniversalTime() : DateTime.MinValue;

        private static (string Id, byte[] Material) Open(IMongoCollection<BsonDocument> keys, string use, BsonDocument document, string secret)
        {
            var kid = document["kid"].AsString;
            // as the first builds stored them
            if (document.TryGetValue("material", out var plain))
                return Reseal(keys, use, document, kid, plain.AsByteArray, secret);

            var material = Open(document, use, kid, secret);
            if (material != null) return (kid, material);

            var previous = Constants.ReadSecret("NOTESNOOK_API_SECRET_PREVIOUS");
            material = previous != null ? Open(document, use, kid, previous) : null;
            if (material != null) return Reseal(keys, use, document, kid, material, secret);

            // making new keys would end every session
            throw new InvalidOperationException($"The token {use} key can't be decrypted with NOTESNOOK_API_SECRET. If it was changed, start once with the old one in NOTESNOOK_API_SECRET_PREVIOUS.");
        }

        private static (string Id, byte[] Material) Reseal(IMongoCollection<BsonDocument> keys, string use, BsonDocument document, string kid, byte[] material, string secret)
        {
            var sealed_ = Seal(use, kid, material, secret);
            sealed_["_id"] = document["_id"];
            sealed_["created_at"] = document.GetValue("created_at", DateTime.UtcNow);
            keys.ReplaceOne(new BsonDocument("_id", document["_id"]), sealed_);
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
