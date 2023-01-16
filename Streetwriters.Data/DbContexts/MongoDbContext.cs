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

using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Streetwriters.Data.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Streetwriters.Data.DbContexts
{
    public class MongoDbContext : IDbContext
    {
        private IMongoDatabase Database { get; set; }
        private MongoClient MongoClient { get; set; }
        private readonly List<Func<IClientSessionHandle, CancellationToken, Task>> _commands;
        private IDbSettings DbSettings { get; set; }
        public MongoDbContext(IDbSettings dbSettings)
        {
            DbSettings = dbSettings;
            Configure();
            // Every command will be stored and it'll be processed at SaveChanges
            _commands = new List<Func<IClientSessionHandle, CancellationToken, Task>>();
        }

        public async Task<int> SaveChanges()
        {
            try
            {
                var count = _commands.Count;

                using (IClientSessionHandle session = await MongoClient.StartSessionAsync())
                {
#if DEBUG
                    await Task.WhenAll(_commands.Select(c => c(session, default(CancellationToken))));
#else
                    await session.WithTransactionAsync(async (handle, token) =>
                    {
                        await Task.WhenAll(_commands.Select(c => c(handle, token)));
                        return true;
                    });
#endif

                }
                return count;
            }
            catch (Exception ex)
            {
                // TODO use Slogger here.
                await Console.Error.WriteLineAsync(ex.ToString());
                return 0;
            }
        }

        private void Configure()
        {
            if (MongoClient != null)
            {
                return;
            }
            var settings = MongoClientSettings.FromConnectionString(DbSettings.ConnectionString);
            settings.MaxConnectionPoolSize = 5000;
            settings.MinConnectionPoolSize = 300;
            MongoClient = new MongoClient(settings);
        }

        public IMongoCollection<T> GetCollection<T>(string databaseName, string collectionName)
        {
            return MongoClient.GetDatabase(databaseName).GetCollection<T>(collectionName, new MongoCollectionSettings()
            {
                AssignIdOnInsert = true,
            });
        }

        public void AddCommand(Func<IClientSessionHandle, CancellationToken, Task> func)
        {
            _commands.Add(func);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        public Task DropDatabaseAsync()
        {
            return MongoClient.DropDatabaseAsync(DbSettings.DatabaseName);
        }
    }
}