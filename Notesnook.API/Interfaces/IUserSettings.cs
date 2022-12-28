using Notesnook.API.Models;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Notesnook.API.Interfaces
{
    public interface IUserSettings : IDocument
    {
        string UserId { get; set; }

        long LastSynced
        {
            get; set;
        }

        EncryptedData VaultKey
        {
            get; set;
        }

        string Salt { get; set; }
    }
}
