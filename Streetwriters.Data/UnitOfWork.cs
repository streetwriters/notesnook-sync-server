using System;
using System.Threading.Tasks;
using Streetwriters.Data.Interfaces;

namespace Streetwriters.Data
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly IDbContext dbContext;

        public UnitOfWork(IDbContext _dbContext)
        {
            dbContext = _dbContext;
        }

        public async Task<bool> Commit()
        {
            var changeAmount = await dbContext.SaveChanges();
            return changeAmount > 0;
        }

        public void Dispose()
        {
            this.dbContext.Dispose();
        }
    }
}