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
using System.Text;
using Geralt;

namespace Streetwriters.Identity.Helpers
{
    internal class PasswordHelper
    {
        public static bool VerifyPassword(string password, string hash)
        {
            return Argon2id.VerifyHash(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(password));
        }

        public static string CreatePasswordHash(string password)
        {
            Span<byte> hash = new(new byte[128]);
            Argon2id.ComputeHash(hash, Encoding.UTF8.GetBytes(password), 3, 65536);
            return Encoding.UTF8.GetString(hash);
        }
    }
}