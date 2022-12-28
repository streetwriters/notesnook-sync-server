using System.Collections.Generic;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Interfaces
{
    public interface IOffer : IDocument
    {
        ApplicationType AppId { get; set; }
        string PromoCode { get; set; }
        PromoCode[] Codes { get; set; }
    }
}
