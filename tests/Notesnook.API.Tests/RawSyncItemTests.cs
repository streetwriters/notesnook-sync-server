using System.Buffers;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Models;

namespace Notesnook.API.Tests;

public class RawSyncItemTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    // the protocol the server negotiates, configured as in Startup
    private static readonly IHubProtocol Protocol = new ServiceCollection()
        .AddLogging()
        .AddSignalR().AddJsonProtocol().Services
        .BuildServiceProvider()
        .GetServices<IHubProtocol>()
        .Single();

    private IMongoCollection<BsonDocument> Documents => db.Collection<BsonDocument>(Collections.NotesKey);

    private static byte[] Write(IHubProtocol protocol, object transfer)
    {
        var buffer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(new InvocationMessage("1", "SendItems", [transfer]), buffer);
        return buffer.WrittenSpan.ToArray();
    }

    private async Task AssertSameOnTheWireAsync(IEnumerable<BsonDocument> documents)
    {
        var ids = new List<ObjectId>();
        foreach (var document in documents)
        {
            document["_id"] = ObjectId.GenerateNewId();
            ids.Add(document["_id"].AsObjectId);
        }
        await Documents.InsertManyAsync(documents);
        var repository = db.CreateScope().Repositories.Notes;
        var typed = await repository.Collection.Find(Builders<SyncItem>.Filter.In("_id", ids)).ToListAsync();
        var raw = await repository.FindItemsByObjectIds(typed[0].UserId!, ids);
        // FindItemsByObjectIds filters by user, so every document here belongs to the same one
        Assert.Equal(typed.Select((i) => i.Id), raw.Select((i) => (ObjectId)typed.First((t) => t.ItemId == i.ItemId).Id));

        var expected = Write(Protocol, new SyncTransferItemV2 { Items = typed, Type = "note", Count = 7 });
        var actual = Write(Protocol, new SyncTransferRawItemV2 { Items = raw, Type = "note", Count = 7 });
        Assert.Equal(expected, actual);
    }

    private static BsonDocument Stored(string userId, Action<SyncItem>? change = null)
    {
        var item = new SyncItem
        {
            UserId = userId,
            ItemId = Guid.NewGuid().ToString("N"),
            Cipher = "QUJDRA-_" + Guid.NewGuid().ToString("N"),
            IV = "aXYtaXYtaXY",
            Length = 1234,
            Version = 5.9,
            Algorithm = Algorithms.Default,
            DateSynced = 1_700_000_000_000
        };
        change?.Invoke(item);
        return item.ToBsonDocument();
    }

    [Fact]
    public async Task Items_written_by_the_server_are_identical_on_the_wire()
    {
        var userId = Guid.NewGuid().ToString("N");
        await AssertSameOnTheWireAsync(
        [
            Stored(userId),
            Stored(userId, (i) => i.KeyVersion = 2),
            Stored(userId, (i) => i.Version = 6),
            Stored(userId, (i) => i.Version = 0.1 + 0.2),
            Stored(userId, (i) => i.Length = long.MaxValue),
            Stored(userId, (i) => i.Length = 0),
            Stored(userId, (i) => i.Cipher = ""),
            Stored(userId, (i) => i.Cipher = new string('x', 300_000)),
            // characters that JSON escapes, and non-ASCII
            Stored(userId, (i) => i.ItemId = "a\"b\\c<d>&'eé中\U0001F600" + Guid.NewGuid()),
            Stored(userId, (i) => i.Algorithm = "xcha-argon2i13-v1+"),
        ]);
    }

    [Fact]
    public async Task Legacy_shapes_are_identical_on_the_wire()
    {
        var userId = Guid.NewGuid().ToString("N");
        BsonDocument Without(params string[] names)
        {
            var document = Stored(userId);
            foreach (var name in names) document.Remove(name);
            return document;
        }
        BsonDocument With(string name, BsonValue value)
        {
            var document = Stored(userId);
            document[name] = value;
            return document;
        }

        await AssertSameOnTheWireAsync(
        [
            Without("DateSynced", "KeyVersion", "IV", "Algorithm", "Version", "Length"),
            Without("Cipher"),
            With("Length", new BsonInt32(42)),
            With("Length", new BsonDouble(42)),
            With("DateSynced", new BsonInt32(1)),
            With("Version", new BsonInt32(5)),
            With("Version", new BsonInt64(6)),
            With("KeyVersion", new BsonInt64(3)),
            With("KeyVersion", new BsonDouble(4)),
            With("KeyVersion", new BsonInt32(5)),
            With("IV", BsonNull.Value),
            With("Cipher", BsonNull.Value),
            With("Algorithm", BsonNull.Value),
            // the driver parses numbers stored as strings, and reads decimals and symbols
            With("Length", new BsonString("12")),
            With("Version", new BsonString("5.9")),
            With("KeyVersion", new BsonString("7")),
            With("Length", new BsonDecimal128(Decimal128.Parse("99"))),
            With("Version", new BsonDecimal128(Decimal128.Parse("6"))),
            With("KeyVersion", new BsonDecimal128(Decimal128.Parse("8"))),
            With("Cipher", BsonSymbolTable.Lookup("c2ltYm9s")),
        ]);
    }

    public static TheoryData<string, BsonValue> Malformed => new()
    {
        { "Length", new BsonString("abc") },
        { "Length", new BsonDecimal128(Decimal128.Parse("1.5")) },
        { "Length", new BsonDouble(1.5) },
        { "Length", BsonNull.Value },
        { "KeyVersion", new BsonInt64(long.MaxValue) },
        { "KeyVersion", new BsonDouble(1.5) },
        { "Version", new BsonString("five") },
        { "KeyVersion", new BsonString("1.5") },
        { "Cipher", new BsonInt32(1) },
        { "ItemId", new BsonBinaryData([1, 2, 3]) },
        { "Algorithm", ObjectId.GenerateNewId() },
        { "Version", new BsonDecimal128(Decimal128.Parse("5.9")) },
        { "IV", new BsonBoolean(true) },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Documents_that_SyncItem_cannot_read_are_rejected_too(string name, BsonValue value)
    {
        var userId = Guid.NewGuid().ToString("N");
        var document = Stored(userId);
        document[name] = value;
        await Documents.InsertOneAsync(document);
        var repository = db.CreateScope().Repositories.Notes;

        await Assert.ThrowsAnyAsync<Exception>(() => repository.Collection.Find(Builders<SyncItem>.Filter.Eq("_id", document["_id"])).ToListAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => repository.FindItemsByObjectIds(userId, [document["_id"].AsObjectId]));
    }
}
