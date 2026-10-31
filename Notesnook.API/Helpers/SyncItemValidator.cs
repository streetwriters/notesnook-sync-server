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
using System.Buffers;
using System.Linq;
using Microsoft.Extensions.Logging;
using Notesnook.API.Models;
using Streetwriters.Common;

namespace Notesnook.API.Helpers
{
    public static class SyncItemValidator
    {
        private static readonly string[] ALGORITHMS = [Algorithms.Default];

        private static readonly SearchValues<byte> Base64Chars =
            SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"u8);

        public static void Validate(PushedSyncItem item, string collectionName, ILogger logger)
        {
            if (item.Length > 15 * 1024 * 1024)
                throw new Exception($"Size of item \"{item.ItemId}\" is too large. Maximum allowed size is 15 MB.");

            if (!ALGORITHMS.Contains(item.Algorithm))
                throw new Exception($"Invalid alg identifier {item.Algorithm}");

            // Handle case where the cipher is corrupted.
            if (item.Cipher == null || !IsBase64(item.Cipher.Span))
            {
                logger.LogError("Corrupted item {ItemId} in collection {CollectionName}. Length: {Length}, Cipher: {Cipher}",
                    item.ItemId, collectionName, item.Length, item.Cipher?.ToString());
                throw new Exception($"Corrupted item \"{item.ItemId}\" in collection \"{collectionName}\". Please report this error to support@streetwriters.co.");
            }

            if (item.ItemId == null)
                throw new Exception($"Item does not have an ItemId.");
        }

        private static bool IsBase64(ReadOnlySpan<byte> value)
        {
            var end = value.Length;
            if (end > 0 && value[end - 1] == '=')
                end--;
            if (end > 0 && value[end - 1] == '=')
                end--;
            if (end == 0)
                return false;
            return value[..end].IndexOfAnyExcept(Base64Chars) < 0;
        }
    }
}
