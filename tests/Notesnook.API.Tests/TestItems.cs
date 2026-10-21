using Notesnook.API.Models;

namespace Notesnook.API.Tests;

public static class TestItems
{
    public static SyncItem Note(string itemId) => new()
    {
        ItemId = itemId,
        Algorithm = Algorithms.Default,
        Cipher = Convert.ToBase64String(new byte[16]),
        IV = Convert.ToBase64String(new byte[24]),
        Length = 16,
        Version = 5.9
    };
}
