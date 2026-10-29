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

using System.Threading;
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
        public Task<long> GetAsync(string userId) =>
            settings.Find((s) => s.UserId == userId).Project((s) => s.Generation).FirstOrDefaultAsync();

        public Task<long> GetAsync(IClientSessionHandle session, string userId, CancellationToken ct) =>
            settings.Find(session, (s) => s.UserId == userId).Project((s) => s.Generation).FirstOrDefaultAsync(ct);

        // a write rather than a read, since only a write conflicts with a reset committed during the transaction
        public Task<long> StampAsync(IClientSessionHandle session, string userId, CancellationToken ct) =>
            settings.FindOneAndUpdateAsync(session,
                Builders<UserSettings>.Filter.Eq((s) => s.UserId, userId),
                Builders<UserSettings>.Update.Inc((s) => s.WriteRevision, 1),
                new FindOneAndUpdateOptions<UserSettings, long> { Projection = Builders<UserSettings>.Projection.Expression((s) => s.Generation) }, ct);

        // items stored before generations existed have no Generation, so they belong to generation 0
        public static FilterDefinition<SyncItem> Filter(long generation) => generation == 0
            ? Builders<SyncItem>.Filter.In("Generation", new BsonValue[] { 0, BsonNull.Value })
            : Builders<SyncItem>.Filter.Eq("Generation", generation);

        public static FilterDefinition<SyncItem> OlderThan(long generation) => Builders<SyncItem>.Filter.Or(
            Builders<SyncItem>.Filter.Lt("Generation", generation),
            Builders<SyncItem>.Filter.Eq("Generation", BsonNull.Value));
    }
}
