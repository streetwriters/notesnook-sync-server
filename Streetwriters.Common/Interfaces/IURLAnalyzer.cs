using System.Collections.Generic;
using System.Threading.Tasks;
using MimeKit;
using MimeKit.Cryptography;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Interfaces
{
    public interface IURLAnalyzer
    {
        Task<bool> IsURLSafeAsync(string uri);
    }
}
