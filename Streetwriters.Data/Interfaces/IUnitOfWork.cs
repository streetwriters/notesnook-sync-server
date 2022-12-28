using System;
using System.Threading.Tasks;

namespace Streetwriters.Data.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        Task<bool> Commit();
    }
}