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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Streetwriters.Data.Interfaces;

namespace Streetwriters.Data.DbContexts
{
    public class MongoDbContext(IMongoClient MongoClient) : IDbContext
    {
        public static IMongoClient CreateMongoDbClient(IDbSettings dbSettings)
        {
            var settings = MongoClientSettings.FromConnectionString(dbSettings.ConnectionString);
            return new MongoClient(settings);
        }

        public static IMongoCollection<T> GetMongoCollection<T>(
            IMongoClient client,
            string databaseName,
            string collectionName
        )
        {
            return client
                .GetDatabase(databaseName)
                .GetCollection<T>(
                    collectionName,
                    new MongoCollectionSettings() { AssignIdOnInsert = true }
                );
        }

        private readonly List<Func<IClientSessionHandle, CancellationToken, Task>> _commands = [];

        public async Task<int> SaveChanges()
        {
            var count = _commands.Count;
            if (count == 0) return 0;

            try
            {
                using IClientSessionHandle session = await MongoClient.StartSessionAsync();
                // a session is not thread-safe, so the commands must run one after another
                await session.WithTransactionAsync(
                    async (handle, token) =>
                    {
                        foreach (var command in _commands)
                            await command(handle, token);
                        return true;
                    }
                );
            }
            finally
            {
                // never replay these commands, whether they were committed or rolled back
                _commands.Clear();
            }
            return count;
        }

        public void AddCommand(Func<IClientSessionHandle, CancellationToken, Task> func)
        {
            _commands.Add(func);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
