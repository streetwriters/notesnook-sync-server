using System.Threading.Tasks;
using WampSharp.V2.Rpc;

namespace Streetwriters.Common.Interfaces
{
    // a call rather than a message, so Identity knows a server's cached tokens are gone once it returns
    public interface ITokenCacheService
    {
        [WampProcedure("co.streetwriters.token_cache.clear")]
        Task ClearAsync(string[] keys);
    }
}
