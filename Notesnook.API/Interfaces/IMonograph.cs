using Notesnook.API.Models;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Interfaces
{
    public interface IMonograph : IDocument
    {
        string Title { get; set; }
        string UserId { get; set; }
        byte[] CompressedContent { get; set; }
        EncryptedData EncryptedContent { get; set; }
        long DatePublished { get; set; }
    }
}
