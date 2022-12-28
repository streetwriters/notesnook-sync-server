using System.Threading.Tasks;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Interfaces
{
    public interface IMessageHandler<T>
    {
        Task Process(T message);
    }
}