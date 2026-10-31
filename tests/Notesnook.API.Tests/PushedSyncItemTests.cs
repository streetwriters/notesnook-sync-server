using System.Buffers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Models;

namespace Notesnook.API.Tests;

public class PushedSyncItemTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private sealed class HubCaller(string userId) : HubCallerContext
    {
        public override string ConnectionId => "connection";
        public override string? UserIdentifier => userId;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim("sub", userId)], "Test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    // what SignalR's JSON protocol binds hub arguments with
    private static readonly JsonSerializerOptions HubOptions = new ServiceCollection()
        .AddLogging()
        .AddSignalR().AddJsonProtocol().Services
        .BuildServiceProvider()
        .GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;

    private static SyncItem Item(Action<SyncItem>? change = null)
    {
        var item = new SyncItem
        {
            ItemId = Guid.NewGuid().ToString("N"),
            Cipher = "QUJDRA-_" + Guid.NewGuid().ToString("N"),
            IV = "aXYtaXYtaXY",
            Length = 1234,
            Version = 5.9,
            Algorithm = Algorithms.Default
        };
        change?.Invoke(item);
        return item;
    }

    public static TheoryData<string, long> Shapes => new()
    {
        { "plain", 0 },
        { "plain", 7 },
        { "keyVersion", 0 },
        { "nullIV", 3 },
        { "emptyIV", 0 },
        { "unicode", 0 },
        { "largeCipher", 2 },
        { "extremes", long.MaxValue },
        { "nullAlgorithm", 0 },
    };

    private static SyncItem Shape(string shape) => shape switch
    {
        "keyVersion" => Item((i) => i.KeyVersion = 2),
        "nullIV" => Item((i) => i.IV = null!),
        "emptyIV" => Item((i) => i.IV = ""),
        "unicode" => Item((i) => { i.ItemId = "a\"b\\c<d>&'eé中\U0001F600\0z"; i.IV = "é中"; }),
        // over the pooling threshold and the large object heap's
        "largeCipher" => Item((i) => i.Cipher = new string('x', 300_000)),
        "extremes" => Item((i) => { i.Length = long.MaxValue; i.Version = 0.1 + 0.2; i.KeyVersion = int.MinValue; }),
        "nullAlgorithm" => Item((i) => i.Algorithm = null!),
        _ => Item(),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Writes_the_document_SyncItem_stores(string shape, long generation)
    {
        var typed = Shape(shape);
        typed.UserId = "user";
        typed.DateSynced = 1_700_000_000_123;
        typed.Generation = generation;
        using var pushed = TestItems.Pushed(typed);

        var buffer = new byte[pushed.BsonSize("user", generation)];
        var length = pushed.WriteBson(buffer, "user", generation, typed.DateSynced);

        Assert.Equal(buffer.Length, length);
        Assert.Equal(typed.ToBson(), buffer);
    }

    public static TheoryData<string> PushedJson => new()
    {
        """{"id":"1","cipher":"QUJD","iv":"aXY","length":3,"v":5.9,"keyVersion":2,"alg":"xcha-argon2i13-v1"}""",
        // the cipher must come out unescaped
        """{"id":"1","cipher":"QUJD\/xé","iv":"aXY","length":3,"v":5,"alg":"x"}""",
        // property names match case-insensitively, as for every hub argument
        """{"ID":"1","Cipher":"QUJD","IV":"aXY","Length":3,"V":6,"KeyVersion":null,"Alg":"x"}""",
        """{"id":"1","cipher":null,"iv":null,"length":0,"v":0,"alg":null}""",
        """{"id":"1"}""",
        """{"id":"1","cipher":"QUJD","dateSynced":5,"userId":"someone","unknown":[1,{"a":2}],"iv":"aXY","length":3,"v":5.9,"alg":"x"}""",
    };

    [Theory]
    [MemberData(nameof(PushedJson))]
    public void Reads_pushed_json_like_SyncItem(string json)
    {
        var typed = JsonSerializer.Deserialize<SyncItem>(json, HubOptions)!;
        // split across several segments, as a message larger than one read arrives
        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var segmentSize in new[] { bytes.Length, 1, 3, 7 })
        {
            var reader = new Utf8JsonReader(Segments(bytes, segmentSize));
            using var pushed = JsonSerializer.Deserialize<PushedSyncItem>(ref reader, HubOptions)!;

            Assert.Equal(typed.ItemId, pushed.ItemId);
            // a missing cipher is "" for SyncItem and null here, and either is rejected as corrupted
            Assert.Equal(typed.Cipher ?? "", pushed.Cipher?.ToString() ?? "");
            Assert.Equal(typed.IV, pushed.IV);
            Assert.Equal(typed.Length, pushed.Length);
            Assert.Equal(typed.Version, pushed.Version);
            Assert.Equal(typed.KeyVersion, pushed.KeyVersion);
            Assert.Equal(typed.Algorithm, pushed.Algorithm);
        }
    }

    [Fact]
    public void A_cipher_that_isnt_a_string_is_rejected_like_SyncItem()
    {
        const string json = """{"id":"1","cipher":5}""";

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<SyncItem>(json, HubOptions));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PushedSyncItem>(json, HubOptions));
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, Segment? previous)
        {
            Memory = memory;
            if (previous == null) return;
            RunningIndex = previous.RunningIndex + previous.Memory.Length;
            previous.Next = this;
        }
    }

    private static ReadOnlySequence<byte> Segments(byte[] bytes, int size)
    {
        Segment? first = null, last = null;
        for (var i = 0; i < bytes.Length; i += size)
        {
            last = new Segment(bytes.AsMemory(i, Math.Min(size, bytes.Length - i)), last);
            first ??= last;
        }
        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    [Theory]
    [InlineData("=")]
    [InlineData("==")]
    [InlineData("QUJD+")]
    [InlineData("QUJD/")]
    [InlineData("QU JD")]
    [InlineData("QUJDé")]
    public async Task Corrupted_ciphers_are_rejected(string cipher)
    {
        var scope = db.CreateScope();
        using var item = TestItems.Pushed(Item((i) => i.Cipher = cipher));

        var error = Assert.Throws<Exception>(() => scope.Repositories.Notes.UpsertMany([item], Guid.NewGuid().ToString("N")));

        Assert.StartsWith("Corrupted item", error.Message);
        Assert.True(await scope.Unit.Commit());
    }

    [Fact]
    public async Task A_push_stores_what_SyncItem_would_and_hands_the_buffers_back()
    {
        var userId = Guid.NewGuid().ToString("N");
        var scope = db.CreateScope();
        await scope.Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings { UserId = userId, Salt = "salt", Generation = 4 });
        var typed = new[] { Item(), Item((i) => i.Cipher = new string('a', 400_000)), Item((i) => i.KeyVersion = 3) };
        var push = new SyncTransferPushItemV2 { Type = "note", Items = [.. typed.Select(TestItems.Pushed)], Count = typed.Length };
        var hub = new SyncV2Hub(scope.Repositories, scope.Unit, scope.Devices, NullLogger<SyncV2Hub>.Instance) { Context = new HubCaller(userId) };
        // an upsert over an item stored before keeps its _id
        var existing = Item((i) => { i.ItemId = typed[0].ItemId; i.UserId = userId; i.Cipher = "b2xk"; });
        await scope.Repositories.Notes.Collection.InsertOneAsync(existing);

        Assert.Equal(1, await hub.PushItems("device", push));

        var stored = await db.CreateScope().Repositories.Notes.Collection.Database.GetCollection<BsonDocument>(Collections.NotesKey)
            .Find(new BsonDocument("UserId", userId)).ToListAsync();
        Assert.Equal(typed.Length, stored.Count);
        foreach (var item in typed)
        {
            var document = stored.Single((d) => d["ItemId"] == item.ItemId);
            item.UserId = userId;
            item.Generation = 4;
            item.DateSynced = document["DateSynced"].AsInt64;
            var expected = item.ToBsonDocument();
            expected.InsertAt(0, new BsonElement("_id", document["_id"]));
            Assert.Equal(expected, document);
        }
        Assert.Equal(existing.Id, stored.Single((d) => d["ItemId"] == existing.ItemId)["_id"].AsObjectId);
        Assert.All(push.Items, (i) => Assert.Throws<ObjectDisposedException>(() => i.Cipher!.Span.Length));
    }

    private sealed class PushBinder : IInvocationBinder
    {
        public IReadOnlyList<Type> GetParameterTypes(string methodName) =>
            [.. typeof(SyncV2Hub).GetMethod(methodName)!.GetParameters().Select((p) => p.ParameterType)];
        public Type GetReturnType(string invocationId) => typeof(object);
        public Type GetStreamItemType(string streamId) => typeof(object);
    }

    [Fact]
    public async Task A_push_invocation_is_bound_by_the_hub_protocol_and_stored()
    {
        var protocol = new ServiceCollection().AddLogging().AddSignalR().AddJsonProtocol().Services
            .BuildServiceProvider().GetServices<IHubProtocol>().Single();
        var userId = Guid.NewGuid().ToString("N");
        var cipher = new string('a', 200_000);
        var json = $$"""{"type":1,"target":"PushItems","invocationId":"1","arguments":["device",{"type":"note","count":2,"items":[{"id":"a","cipher":"{{cipher}}","iv":"aXY","length":3,"v":5.9,"alg":"{{Algorithms.Default}}"},{"id":"b","cipher":"QUJD","iv":"aXY","length":3,"v":5.9,"keyVersion":1,"alg":"{{Algorithms.Default}}"}]}]}""" + "\u001e";
        var message = Segments(Encoding.UTF8.GetBytes(json), 4096);

        Assert.True(protocol.TryParseMessage(ref message, new PushBinder(), out var parsed));
        var arguments = ((InvocationMessage)parsed!).Arguments;
        var scope = db.CreateScope();
        var hub = new SyncV2Hub(scope.Repositories, scope.Unit, scope.Devices, NullLogger<SyncV2Hub>.Instance) { Context = new HubCaller(userId) };
        Assert.Equal(1, await hub.PushItems((string)arguments[0]!, (SyncTransferPushItemV2)arguments[1]!));

        var stored = await db.CreateScope().Repositories.Notes.Collection.Find((n) => n.UserId == userId).ToListAsync();
        Assert.Equal(cipher, stored.Single((n) => n.ItemId == "a").Cipher);
        Assert.Equal(1, stored.Single((n) => n.ItemId == "b").KeyVersion);
    }
}
