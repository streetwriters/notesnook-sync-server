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
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Models;

namespace Notesnook.API.Repositories
{
    /// <summary>
    /// Every reset starts a new generation of the user's sync items. Items from an older one are hidden until UserPurgeJob deletes them.
    /// </summary>
    public class UserGenerations(IMongoCollection<UserSettings> settings)
    {
        private readonly Dictionary<string, long> pinned = [];

        /// <summary>
        /// The user's generation for this scope: read once, or pinned, so a request or hub connection never mixes generations.
        /// </summary>
        public async Task<long> GetAsync(string userId)
        {
            if (!pinned.TryGetValue(userId, out var generation))
                pinned[userId] = generation = await ReadAsync(userId);
            return generation;
        }

        public void Pin(string userId, long generation) => pinned[userId] = generation;

        // read again, for work that may have outlasted a reset
        public async Task<bool> IsCurrentAsync(string userId) => await GetAsync(userId) == await ReadAsync(userId);

        private Task<long> ReadAsync(string userId) =>
            settings.Find((s) => s.UserId == userId).Project((s) => s.Generation).FirstOrDefaultAsync();

        // items stored before generations existed have no Generation, so they belong to generation 0
        // null is written as raw BSON: a typed filter first tries to convert it to long, and that throws and is swallowed on every query
        public static FilterDefinition<SyncItem> Filter(long generation) => generation == 0
            ? new BsonDocument("Generation", new BsonDocument("$in", new BsonArray { 0L, BsonNull.Value }))
            : Builders<SyncItem>.Filter.Eq("Generation", generation);

        public static FilterDefinition<SyncItem> OlderThan(long generation) => Builders<SyncItem>.Filter.Or(
            Builders<SyncItem>.Filter.Lt("Generation", generation),
            new BsonDocument("Generation", BsonNull.Value));
    }
}
