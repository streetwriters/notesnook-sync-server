
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Streetwriters.Data.Interfaces
{
    public interface IDbContext : IDisposable
    {
        void AddCommand(Func<IClientSessionHandle, CancellationToken, Task> func);
        Task<int> SaveChanges();
        IMongoCollection<T> GetCollection<T>(string databaseName, string collectionName);
    }
}