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
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Helpers;

namespace Streetwriters.Identity.Services
{
    public class Argon2PasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : User
    {
        const long MAX_PASSWORD_LENGTH = 1024 * 2;
        public string HashPassword(TUser user, string password)
        {
            if (password.Length > MAX_PASSWORD_LENGTH)
                throw new Exception("Password is too long.");
            ArgumentNullException.ThrowIfNullOrEmpty(password, nameof(password));
            return PasswordHelper.CreatePasswordHash(password);
        }

        public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(hashedPassword, nameof(hashedPassword));
            ArgumentNullException.ThrowIfNullOrEmpty(providedPassword, nameof(providedPassword));

            return PasswordHelper.VerifyPassword(providedPassword, hashedPassword) ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
        }
    }
}