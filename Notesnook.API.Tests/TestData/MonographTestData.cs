using MongoDB.Bson;
using Notesnook.API.Models;

namespace Notesnook.API.Tests.TestData
{
  public static class MonographTestData
  {
    public const string TestUserId = "test-user-123";
    public const string TestDeviceId = "test-device-456";
    public const string TestJtiToken = "test-jti-token";

    public static Monograph CreateMonograph(
        string? itemId = null,
        string? userId = null,
        bool deleted = false,
        bool isExisting = false,
        bool selfDestruct = false,
        string? title = null)
    {
      return new Monograph
      {
        Id = ObjectId.GenerateNewId().ToString(),
        ItemId = itemId ?? Guid.NewGuid().ToString(),
        Title = title ?? "Test Monograph",
        UserId = userId ?? TestUserId,
        Content = "This is test content for the monograph.",
        Deleted = deleted,
        DatePublished = isExisting ? DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds() : 0,
        SelfDestruct = selfDestruct,
        EncryptedContent = null,
        CompressedContent = [1, 2, 3, 4, 5],
        Password = null
      };
    }

    public static Monograph CreateEncryptedMonograph()
    {
      var monograph = CreateMonograph();
      monograph.Content = null;
      monograph.CompressedContent = null;
      monograph.EncryptedContent = new EncryptedData
      {
        Cipher = "encrypted-test-content",
        IV = "test-iv",
        Salt = "test-salt"
      };
      return monograph;
    }

    public static Monograph CreateLargeEncryptedMonograph()
    {
      var monograph = CreateMonograph();
      monograph.Content = null;
      monograph.CompressedContent = null;
      monograph.EncryptedContent = new EncryptedData
      {
        Cipher = new string('*', 16 * 1024 * 1024),
        IV = "test-iv",
        Salt = "test-salt"
      };
      return monograph;
    }

    public static IEnumerable<string> MonographIds()
    {
      return new List<string> { "id1", "id2", "id3" };
    }
  }
}
