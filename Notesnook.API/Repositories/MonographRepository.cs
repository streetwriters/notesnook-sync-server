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
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Repositories
{
  public class MonographRepository : Repository<Monograph>, IMonographRepository
  {
    public MonographRepository(IDbContext dbContext, IMongoCollection<Monograph> collection)
        : base(dbContext, collection)
    {
    }

    private static FilterDefinition<Monograph> CreateMonographFilter(string userId, Monograph monograph)
    {
      var userIdFilter = Builders<Monograph>.Filter.Eq("UserId", userId);
      monograph.ItemId ??= monograph.Id;
      return ObjectId.TryParse(monograph.ItemId, out ObjectId id)
      ? Builders<Monograph>.Filter
          .And(userIdFilter,
              Builders<Monograph>.Filter.Or(
                  Builders<Monograph>.Filter.Eq("_id", id),
                  Builders<Monograph>.Filter.Eq("ItemId", monograph.ItemId)
              )
          )
      : Builders<Monograph>.Filter
          .And(userIdFilter,
              Builders<Monograph>.Filter.Eq("ItemId", monograph.ItemId)
          );
    }

    private static FilterDefinition<Monograph> CreateMonographFilter(string itemId)
    {
      return ObjectId.TryParse(itemId, out ObjectId id)
      ? Builders<Monograph>.Filter.Or(
          Builders<Monograph>.Filter.Eq("_id", id),
          Builders<Monograph>.Filter.Eq("ItemId", itemId))
      : Builders<Monograph>.Filter.Eq("ItemId", itemId);
    }

    public async Task<Monograph> FindByUserAndItemAsync(string userId, Monograph monograph)
    {
      var result = await Collection.FindAsync(CreateMonographFilter(userId, monograph), new FindOptions<Monograph>
      {
        Limit = 1
      });
      return await result.FirstOrDefaultAsync();
    }

    public async Task<Monograph> FindByItemIdAsync(string itemId)
    {
      var result = await Collection.FindAsync(CreateMonographFilter(itemId), new FindOptions<Monograph>
      {
        Limit = 1
      });
      return await result.FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<string>> GetUserMonographIdsAsync(string userId)
    {
      var monographs = (await Collection.FindAsync(
              Builders<Monograph>.Filter.And(
                  Builders<Monograph>.Filter.Eq("UserId", userId),
                  Builders<Monograph>.Filter.Ne("Deleted", true)
              )
         , new FindOptions<Monograph, ObjectWithId>
         {
           Projection = Builders<Monograph>.Projection.Include("_id").Include("ItemId"),
         })).ToEnumerable();
      return monographs.Select((m) => m.ItemId ?? m.Id);
    }

    public async Task<ReplaceOneResult> PublishOrUpdateAsync(string userId, Monograph monograph)
    {
      return await Collection.ReplaceOneAsync(
          CreateMonographFilter(userId, monograph),
          monograph,
          new ReplaceOptions { IsUpsert = true }
      );
    }

    public async Task<UpdateResult> UpdateMonographAsync(string userId, Monograph monograph)
    {
      return await Collection.UpdateOneAsync(
          CreateMonographFilter(userId, monograph),
          Builders<Monograph>.Update
          .Set(m => m.DatePublished, monograph.DatePublished)
          .Set(m => m.CompressedContent, monograph.CompressedContent)
          .Set(m => m.EncryptedContent, monograph.EncryptedContent)
          .Set(m => m.SelfDestruct, monograph.SelfDestruct)
          .Set(m => m.Title, monograph.Title)
          .Set(m => m.Password, monograph.Password)
      );
    }

    public async Task<ReplaceOneResult> SoftDeleteAsync(string userId, Monograph monograph, string itemId)
    {
      return await Collection.ReplaceOneAsync(
          CreateMonographFilter(userId, monograph),
          new Monograph
          {
            ItemId = itemId,
            Id = monograph.Id,
            Deleted = true,
            UserId = monograph.UserId
          }
      );
    }

    public async Task<ReplaceOneResult> SelfDestructAsync(Monograph monograph, string itemId)
    {
      return await Collection.ReplaceOneAsync(
          CreateMonographFilter(itemId),
          new Monograph
          {
            ItemId = itemId,
            Id = monograph.Id,
            Deleted = true,
            UserId = monograph.UserId
          }
      );
    }

    public new async Task<IEnumerable<Monograph>> FindAsync(Expression<System.Func<Monograph, bool>> filterExpression)
    {
      return await base.FindAsync(filterExpression);
    }

    public new void DeleteMany(Expression<System.Func<Monograph, bool>> filterExpression)
    {
      base.DeleteMany(filterExpression);
    }
  }
}
