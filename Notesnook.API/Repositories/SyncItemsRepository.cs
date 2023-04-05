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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using Notesnook.API.Models;
using Streetwriters.Data.Attributes;

namespace Notesnook.API.Repositories
{
    public class SyncItemsRepository<T> where T : SyncItem
    {
        const string BASE_DATA_DIR = "data";
        private string GetCollectionName()
        {
            var attribute = (BsonCollectionAttribute)typeof(T).GetCustomAttributes(
                    typeof(BsonCollectionAttribute),
                    true).FirstOrDefault();
            if (string.IsNullOrEmpty(attribute.CollectionName) || string.IsNullOrEmpty(attribute.DatabaseName)) throw new Exception("Could not get a valid collection or database name.");
            return attribute.CollectionName;
        }

        private string GetUserDirectoryPath(string userId)
        {
            return System.IO.Path.Join(BASE_DATA_DIR, userId, GetCollectionName());
        }

        private IEnumerable<string> EnumerateItems(string userId, string searchPattern = "*")
        {
            try
            {
                return System.IO.Directory.EnumerateFiles(GetUserDirectoryPath(userId), searchPattern, System.IO.SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return new string[] { };
            }
        }

        private string FindItemById(string userId, string id)
        {
            try
            {
                var files = Directory.GetFiles(GetUserDirectoryPath(userId), $"{id}-*", System.IO.SearchOption.TopDirectoryOnly);
                return files.Length > 0 ? files[0] : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<IEnumerable<string>> GetItemsSyncedAfterAsync(string userId, long timestamp)
        {
            var items = new List<string>();
            await Parallel.ForEachAsync(EnumerateItems(userId), async (file, ct) =>
            {
                var parts = file.Split("-");
                var id = parts[0];
                var dateSynced = long.Parse(parts[1]);
                if (dateSynced > timestamp) items.Add(await File.ReadAllTextAsync(file));
            });
            return items;
        }

        public void DeleteByUserId(string userId)
        {
            Directory.Delete(GetUserDirectoryPath(userId), true);
        }

        public async Task UpsertAsync(string id, string item, string userId, long dateSynced)
        {
            Directory.CreateDirectory(GetUserDirectoryPath(userId));
            var oldPath = FindItemById(userId, id);
            var newPath = Path.Join(GetUserDirectoryPath(userId), $"{id}-{dateSynced}");
            await File.WriteAllTextAsync(newPath, item);
            if (oldPath != null) File.Delete(oldPath);
        }
    }
}