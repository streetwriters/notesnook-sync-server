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

using System.Threading.Tasks;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Repositories;

namespace Notesnook.API.Services
{
    /// <summary>
    /// Deletes a user's data from the generations before their latest reset. Every step is idempotent, so a failed purge is retried.
    /// </summary>
    public class ResetPurgeService(ISyncItemsRepositoryAccessor repositories, SyncDeviceService syncDeviceService)
    {
        /// <summary>
        /// Everything but the sync items, which can be too many to delete in a request.
        /// </summary>
        public async Task PurgeSmallCollectionsAsync(string userId, long generation)
        {
            await repositories.InboxApiKey.Collection.DeleteManyAsync(OlderThan<InboxApiKey>(userId, generation));
            await repositories.InboxItems.Collection.DeleteManyAsync(OlderThan<InboxSyncItem>(userId, generation));
            await repositories.Monographs.Collection.DeleteManyAsync(OlderThan<Monograph>(userId, generation));
            await syncDeviceService.DeleteOlderDevicesAsync(userId, generation);
        }

        public async Task PurgeAsync(string userId, long generation)
        {
            await PurgeSmallCollectionsAsync(userId, generation);
            foreach (var collection in repositories.SyncItemCollections)
                await collection.Repository.DeleteOlderGenerationsAsync(userId, generation);

            // only the generation we purged, so a reset made meanwhile stays pending
            await repositories.UsersSettings.Collection.UpdateOneAsync(
                (s) => s.UserId == userId && s.Generation == generation,
                Builders<UserSettings>.Update.Unset((s) => s.PurgePendingSince));
        }

        private static FilterDefinition<T> OlderThan<T>(string userId, long generation) =>
            Builders<T>.Filter.Eq("UserId", userId) & UserGenerations.OlderThan<T>(generation);
    }
}
