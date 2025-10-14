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
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Streetwriters.Data.Interfaces;

namespace Streetwriters.Data.Repositories
{
    public class Repository<TEntity> where TEntity : class
    {
        protected readonly IDbContext dbContext;
        public IMongoCollection<TEntity> Collection { get; set; }

        public Repository(IDbContext _dbContext, IMongoCollection<TEntity> collection)
        {
            dbContext = _dbContext;
            Collection = collection;
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

        public virtual async Task<TEntity> GetAsync(ObjectId id)
        {
            var data = await Collection.FindAsync(Builders<TEntity>.Filter.Eq("_id", id));
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

        public virtual async Task<long> CountAsync(Expression<Func<TEntity, bool>> filterExpression)
        {
            return await Collection.CountDocumentsAsync(filterExpression);
        }

        public virtual void Update(ObjectId id, TEntity obj)
        {
            dbContext.AddCommand((handle, ct) => Collection.ReplaceOneAsync(handle, Builders<TEntity>.Filter.Eq("_id", id), obj, cancellationToken: ct));
        }

        public virtual Task UpdateAsync(ObjectId id, TEntity obj)
        {
            return Collection.ReplaceOneAsync(Builders<TEntity>.Filter.Eq("_id", id), obj);
        }

        public virtual void DeleteById(ObjectId id)
        {
            dbContext.AddCommand((handle, ct) => Collection.DeleteOneAsync(handle, Builders<TEntity>.Filter.Eq("_id", id), cancellationToken: ct));
        }

        public virtual Task DeleteByIdAsync(ObjectId id)
        {
            return Collection.DeleteOneAsync(Builders<TEntity>.Filter.Eq("_id", id));
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