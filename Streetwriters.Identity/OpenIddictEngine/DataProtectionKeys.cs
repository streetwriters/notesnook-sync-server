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
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using MongoDB.Bson;
using MongoDB.Driver;
using Streetwriters.Common;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// The ASP.NET data protection keys, which sign password reset and email confirmation tokens. In the database so every
    /// instance validates the others' tokens and a restart doesn't void the links already sent.
    /// </summary>
    public class DataProtectionKeys(IMongoDatabase database) : IXmlRepository
    {
        private readonly IMongoCollection<BsonDocument> keys = database.GetCollection<BsonDocument>("dataprotection.keys");

        public IReadOnlyCollection<XElement> GetAllElements() =>
            keys.Find(FilterDefinition<BsonDocument>.Empty).ToList().Select((d) => XElement.Parse(d["xml"].AsString)).ToList();

        public void StoreElement(XElement element, string friendlyName) =>
            keys.ReplaceOne(new BsonDocument("_id", friendlyName),
                new BsonDocument { ["_id"] = friendlyName, ["xml"] = element.ToString(SaveOptions.DisableFormatting), ["created_at"] = DateTime.UtcNow },
                new ReplaceOptions { IsUpsert = true });
    }

    /// <summary>
    /// Encrypts the data protection keys with NOTESNOOK_API_SECRET, like TokenKeys, so backups can't mint reset tokens.
    /// </summary>
    /// <remarks>
    /// After NOTESNOOK_API_SECRET changes the old keys can't be read and new ones are made; only links sent before then stop working.
    /// </remarks>
    public class DataProtectionKeyEncryptor : IXmlEncryptor, IXmlDecryptor
    {
        private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("dataprotection");

        public EncryptedXmlInfo Encrypt(XElement plaintextElement)
        {
            var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];
            using var aes = new AesGcm(Key(), tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
            var encrypted = new XElement("encryptedKey",
                new XAttribute("nonce", Convert.ToBase64String(nonce)),
                new XAttribute("tag", Convert.ToBase64String(tag)),
                Convert.ToBase64String(ciphertext));
            return new EncryptedXmlInfo(encrypted, typeof(DataProtectionKeyEncryptor));
        }

        public XElement Decrypt(XElement encryptedElement)
        {
            var ciphertext = Convert.FromBase64String(encryptedElement.Value);
            var tag = Convert.FromBase64String(encryptedElement.Attribute("tag")!.Value);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(Key(), tag.Length);
            aes.Decrypt(Convert.FromBase64String(encryptedElement.Attribute("nonce")!.Value), ciphertext, tag, plaintext, AssociatedData);
            return XElement.Parse(Encoding.UTF8.GetString(plaintext));
        }

        private static byte[] Key() =>
            HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(Constants.NOTESNOOK_API_SECRET), 32,
                info: Encoding.UTF8.GetBytes("streetwriters.identity.dataprotection.keys"));
    }
}
