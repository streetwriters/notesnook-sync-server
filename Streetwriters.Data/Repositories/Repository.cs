using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Streetwriters.Data.Attributes;
using Streetwriters.Data.Interfaces;

namespace Streetwriters.Data.Repositories
{
    public class Repository<TEntity> where TEntity : class
    {
        protected readonly IDbContext dbContext;
        protected IMongoCollection<TEntity> Collection { get; set; }

        public Repository(IDbContext _dbContext)
        {
            dbContext = _dbContext;
            Collection = GetCollection();
        }

        private protected IMongoCollection<TEntity> GetCollection()
        {
            var attribute = (BsonCollectionAttribute)typeof(TEntity).GetCustomAttributes(
                    typeof(BsonCollectionAttribute),
                    true).FirstOrDefault();
            if (string.IsNullOrEmpty(attribute.CollectionName) || string.IsNullOrEmpty(attribute.DatabaseName)) throw new Exception("Could not get a valid collection or database name.");
            return dbContext.GetCollection<TEntity>(attribute.DatabaseName, attribute.CollectionName);
        }


        public virtual void Insert(TEntity obj)
        {
            dbContext.AddCommand((handle, ct) => Collection.InsertOneAsync(handle, obj, null, ct));
        }


        public virtual Task InsertAsync(TEntity obj)
        {
            return Collection.InsertOneAsync(obj);
        }

        public virtual void Upsert(TEntity obj, Expression<Func<TEntity, bool>> filterExpression)
        {
            dbContext.AddCommand((handle, ct) => Collection.ReplaceOneAsync(handle, filterExpression, obj, new ReplaceOptions { IsUpsert = true }, ct));
        }

        public virtual Task UpsertAsync(TEntity obj, Expression<Func<TEntity, bool>> filterExpression)
        {
            return Collection.ReplaceOneAsync(filterExpression, obj, new ReplaceOptions { IsUpsert = true });
        }

        public virtual async Task<TEntity> FindOneAsync(Expression<Func<TEntity, bool>> filterExpression)
        {
            var data = await Collection.FindAsync(filterExpression);
            return data.FirstOrDefault();
        }

        public virtual async Task<TEntity> GetAsync(string id)
        {
            var data = await Collection.FindAsync(Builders<TEntity>.Filter.Eq("_id", ObjectId.Parse(id)));
            return data.FirstOrDefault();
        }

        public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> filterExpression)
        {
            var data = await Collection.FindAsync(filterExpression);
            return data.ToList();
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            var all = await Collection.FindAsync(Builders<TEntity>.Filter.Empty);
            return all.ToList();
        }

        public virtual void Update(string id, TEntity obj)
        {
            dbContext.AddCommand((handle, ct) => Collection.ReplaceOneAsync(handle, Builders<TEntity>.Filter.Eq("_id", ObjectId.Parse(id)), obj, cancellationToken: ct));
        }

        public virtual Task UpdateAsync(string id, TEntity obj)
        {
            return Collection.ReplaceOneAsync(Builders<TEntity>.Filter.Eq("_id", ObjectId.Parse(id)), obj);
        }

        public virtual void DeleteById(string id)
        {
            dbContext.AddCommand((handle, ct) => Collection.DeleteOneAsync(handle, Builders<TEntity>.Filter.Eq("_id", ObjectId.Parse(id)), cancellationToken: ct));
        }

        public virtual Task DeleteByIdAsync(string id)
        {
            return Collection.DeleteOneAsync(Builders<TEntity>.Filter.Eq("_id", ObjectId.Parse(id)));
        }

        public virtual void Delete(Expression<Func<TEntity, bool>> filterExpression)
        {
            dbContext.AddCommand((handle, ct) => Collection.DeleteOneAsync(handle, filterExpression, cancellationToken: ct));
        }

        public virtual void DeleteMany(Expression<Func<TEntity, bool>> filterExpression)
        {
            dbContext.AddCommand((handle, ct) => Collection.DeleteManyAsync(handle, filterExpression, cancellationToken: ct));
        }

        public virtual Task DeleteAsync(Expression<Func<TEntity, bool>> filterExpression)
        {
            return Collection.DeleteOneAsync(filterExpression);
        }

        public virtual Task DeleteManyAsync(Expression<Func<TEntity, bool>> filterExpression)
        {
            return Collection.DeleteManyAsync(filterExpression);
        }
    }
}