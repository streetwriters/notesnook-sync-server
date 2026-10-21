using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Streetwriters.Common;
using WampSharp.V2;
using WampSharp.V2.Realm;

namespace Notesnook.API.Tests;

public class WampServerTests
{
    private const string Realm = "messages";
    private const string Topic = "co.streetwriters.test";
    private const string MessageTopic = "co.streetwriters.test.message";

    public class TestMessage
    {
        public string? UserId { get; set; }
        public string[]? Ids { get; set; }
    }

    static WampServerTests()
    {
        // the client derives its auth token from this; the test router doesn't check it
        Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET", "test-secret");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static WampServer Client(int port) => new()
    {
        Endpoint = "/wamp",
        Port = port,
        Address = $"ws://127.0.0.1:{port}/wamp",
        Realm = Realm
    };

    private sealed class Router : IDisposable
    {
        private readonly DefaultWampHost host;
        public IWampHostedRealm Realm { get; }
        public int Sessions => sessions;
        private int sessions;
        public ConcurrentQueue<string> Received { get; } = new();
        public ConcurrentQueue<TestMessage> ReceivedMessages { get; } = new();

        public Router(int port)
        {
            host = new DefaultWampHost($"ws://127.0.0.1:{port}/wamp");
            Realm = host.RealmContainer.GetRealmByName(WampServerTests.Realm);
            Realm.SessionCreated += (_, _) => Interlocked.Increment(ref sessions);
            // the subjects error when the host shuts down
            Realm.Services.GetSubject<string>(Topic).Subscribe(m => Received.Enqueue(m), _ => { });
            Realm.Services.GetSubject<TestMessage>(MessageTopic).Subscribe(m => ReceivedMessages.Enqueue(m), _ => { });
            host.Open();
        }

        public void Dispose() => host.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
            await Task.Delay(100, cts.Token);
    }

    [Fact]
    public async Task Publishing_times_out_when_the_server_is_down()
    {
        var client = Client(FreePort());

        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PublishMessageAsync(Topic, "hello"));
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Typed_subscribers_receive_published_messages()
    {
        var port = FreePort();
        using var router = new Router(port);
        var client = Client(port);

        await client.PublishMessageAsync(MessageTopic, new TestMessage { UserId = "user", Ids = ["a", "b"] });
        await client.PublishMessagesAsync(MessageTopic, new[] { new TestMessage { UserId = "1" }, new TestMessage { UserId = "2" } });

        await WaitUntilAsync(() => router.ReceivedMessages.Count == 3, TimeSpan.FromSeconds(10));
        var received = router.ReceivedMessages.ToArray();
        Assert.Equal("user", received[0].UserId);
        Assert.Equal(["a", "b"], received[0].Ids!);
        Assert.Equal(["1", "2"], received.Skip(1).Select(m => m.UserId));
    }

    [Fact]
    public async Task Concurrent_publishes_share_one_connection()
    {
        var port = FreePort();
        using var router = new Router(port);
        var client = Client(port);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => client.PublishMessageAsync(Topic, $"{i}")));

        await WaitUntilAsync(() => router.Received.Count == 20, TimeSpan.FromSeconds(10));
        Assert.Equal(1, router.Sessions);
    }
}
