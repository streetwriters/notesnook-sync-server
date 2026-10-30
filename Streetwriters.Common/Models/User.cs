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



using System.Collections.Generic;
using AspNetCore.Identity.Mongo.Model;
using MongoDB.Bson.Serialization.Attributes;

namespace Streetwriters.Common.Models
{
    public class User : MongoUser
    {
        /// <summary>
        /// The end of the lockout during an account reset, so a lockout that ends then is reported as the reset.
        /// </summary>
        [BsonIgnoreIfNull]
        public long? ResetLockoutEnd { get; set; }

        /// <summary>
        /// Keys of ended sessions whose cached tokens some server may still hold, kept until a clear succeeds.
        /// </summary>
        [BsonIgnoreIfNull]
        public List<string>? PendingCacheKeys { get; set; }
    }
}
