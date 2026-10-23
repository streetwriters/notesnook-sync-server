using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using EphemeralMongo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Streetwriters.Common;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Services;
using WampSharp.V2;

// the hosts share process-wide environment variables, so their tests can't run at the same time
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Streetwriters.Identity.Tests;

static class TestEnvironment
{
    public static readonly int NotesnookWampPort = FreePort();
    public static readonly int MessengerWampPort = FreePort();
    // one per process: every host publishes to the same ports
    public static readonly WampRecorder NotesnookServer = new(NotesnookWampPort);
    public static readonly WampRecorder MessengerServer = new(MessengerWampPort);
    public const string ApiSecret = "test-secret";

    // Servers and WampServers read these once, in static initializers
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET", ApiSecret);
        Environment.SetEnvironmentVariable("SELF_HOSTED", "1");
        Environment.SetEnvironmentVariable("IDENTITY_SERVER_HOST", "localhost");
        Environment.SetEnvironmentVariable("IDENTITY_SERVER_PORT", "80");
        Environment.SetEnvironmentVariable("NOTESNOOK_SERVER_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("NOTESNOOK_WAMP_PORT", NotesnookWampPort.ToString());
        Environment.SetEnvironmentVariable("SSE_SERVER_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("SSE_WAMP_PORT", MessengerWampPort.ToString());
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>The clock of both token servers: IdentityServer4 reads ISystemClock, OpenIddict TimeProvider.</summary>
public sealed class TestClock : TimeProvider, ISystemClock
{
    private TimeSpan offset;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + offset;
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public void Advance(TimeSpan by) => offset += by;
    public void Set(DateTimeOffset now) => offset = now - DateTimeOffset.UtcNow;
    public void Reset() => offset = TimeSpan.Zero;
}

/// <summary>A WAMP router standing in for another server, recording what Identity publishes to it.</summary>
public sealed class WampRecorder : IDisposable
{
    private readonly DefaultWampHost host;
    public ConcurrentQueue<ClearCacheMessage> ClearCache { get; } = new();
    public ConcurrentQueue<SendSSEMessage> SSE { get; } = new();

    public WampRecorder(int port)
    {
        host = new DefaultWampHost($"ws://127.0.0.1:{port}/wamp");
        var realm = host.RealmContainer.GetRealmByName("messages");
        // the subjects error when the host shuts down
        realm.Services.GetSubject<ClearCacheMessage>(IdentityServerTopics.ClearCacheTopic).Subscribe(ClearCache.Enqueue, _ => { });
        realm.Services.GetSubject<SendSSEMessage>(MessengerServerTopics.SendSSETopic).Subscribe(SSE.Enqueue, _ => { });
        host.Open();
    }

    public void Clear()
    {
        ClearCache.Clear();
        SSE.Clear();
    }

    public void Dispose() => host.Dispose();
}

public class RecordingProxy<T> : DispatchProxy where T : class
{
    public ConcurrentQueue<(string Method, object?[] Args)> Calls { get; } = new();

    public static (T Proxy, RecordingProxy<T> Recorder) Create()
    {
        var proxy = Create<T, RecordingProxy<T>>();
        return (proxy, (RecordingProxy<T>)(object)proxy);
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Calls.Enqueue((method!.Name, args ?? []));
        var returnType = method.ReturnType;
        if (returnType == typeof(Task)) return Task.CompletedTask;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var result = returnType.GenericTypeArguments[0].IsValueType ? Activator.CreateInstance(returnType.GenericTypeArguments[0]) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(returnType.GenericTypeArguments[0]).Invoke(null, [result]);
        }
        return null;
    }
}

/// <summary>
/// The whole Identity server, in-process, against a throwaway database, on one of the token servers.
/// Shared by the tests of its collection.
/// </summary>
public abstract class IdentityHost : IAsyncLifetime
{
    /// <summary>The value of <see cref="Config.ENGINE_SETTING"/>; null for the default, IdentityServer4.</summary>
    public abstract string? Engine { get; }
    public bool IsOpenIddict => Engine == Config.OPENIDDICT_ENGINE;

    private static readonly IMongoRunner Mongo = StartMongo();

    // EphemeralMongo only stops mongod when the runner is disposed
    private static IMongoRunner StartMongo()
    {
        var runner = MongoRunner.Run(new MongoRunnerOptions { Version = MongoVersion.V8 });
        AppDomain.CurrentDomain.ProcessExit += (_, _) => runner.Dispose();
        return runner;
    }

    public string DatabaseName { get; init; } = "identity_host_" + Guid.NewGuid().ToString("N");
    /// <summary>Leave the database when the host stops, for another host to carry on with.</summary>
    public bool KeepDatabase { get; init; }
    public IMongoDatabase Database => new MongoClient(Mongo.ConnectionString).GetDatabase(DatabaseName);
    public TestClock Clock { get; } = new();
    public WampRecorder NotesnookServer => TestEnvironment.NotesnookServer;
    public WampRecorder MessengerServer => TestEnvironment.MessengerServer;
    public RecordingProxy<ITemplatedEmailSender> Emails { get; private set; } = null!;
    public IHost Host { get; private set; } = null!;
    private readonly string contentRoot = Directory.CreateTempSubdirectory("identity_host_").FullName;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("MONGODB_CONNECTION_STRING", new MongoUrlBuilder(Mongo.ConnectionString) { DatabaseName = DatabaseName }.ToString());
        Environment.SetEnvironmentVariable("MONGODB_DATABASE_NAME", DatabaseName);

        // as in production, where the indexes are created separately
        var users = Database.GetCollection<MongoDB.Bson.BsonDocument>("users");
        foreach (var field in new[] { "NormalizedEmail", "NormalizedUserName" })
            await users.Indexes.CreateOneAsync(new CreateIndexModel<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument(field, 1), new CreateIndexOptions { Unique = true }));

        var (emails, emailRecorder) = RecordingProxy<ITemplatedEmailSender>.Create();
        var (sms, _) = RecordingProxy<ISMSSender>.Create();
        Emails = emailRecorder;

        Host = Program.CreateHostBuilder([])
            .UseContentRoot(contentRoot)
            .ConfigureAppConfiguration((configuration) => configuration.AddInMemoryCollection([KeyValuePair.Create(Config.ENGINE_SETTING, Engine)]))
            .ConfigureWebHost((builder) => builder
                .UseTestServer()
                .ConfigureTestServices((services) =>
                {
                    services.AddSingleton(emails);
                    services.AddSingleton(sms);
                    services.AddSingleton<ISystemClock>(Clock);
                    services.AddSingleton<TimeProvider>(Clock);
                    // the disposable domains list is fetched from GitHub; tests must not need the network
                    services.AddHttpClient<EmailAddressValidator>().ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());
                    // the MFA step tokens are validated through discovery, which must reach the test server
                    services.Configure<JwtBearerOptions>("Bearer+jwt", (options) => options.BackchannelHttpHandler = new TestServerHandler(() => Host!));
                }))
            .Build();
        await Host.StartAsync();
    }

    public HttpClient CreateClient() => Host.GetTestServer().CreateClient();

    public async Task<T> WithServicesAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Host.Services.CreateScope();
        // WAMP calls run inside the WebSocket request, and IdentityServer4 reads the issuer from it
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        try { return await action(scope.ServiceProvider); }
        finally { accessor.HttpContext = previous; }
    }

    public Task<User?> FindUserAsync(string email) => WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().FindByEmailAsync(email));

    public void Reset()
    {
        Clock.Reset();
        NotesnookServer.Clear();
        MessengerServer.Clear();
        Emails.Calls.Clear();
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        if (!KeepDatabase) await new MongoClient(Mongo.ConnectionString).DropDatabaseAsync(DatabaseName);
        Directory.Delete(contentRoot, true);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }

    private sealed class TestServerHandler(Func<IHost> host) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InnerHandler ??= host().GetTestServer().CreateHandler();
            return base.SendAsync(request, cancellationToken);
        }
    }
}

public sealed class IdentityServer4Host : IdentityHost
{
    public override string? Engine => null;
}

public sealed class OpenIddictHost : IdentityHost
{
    public override string? Engine => Config.OPENIDDICT_ENGINE;
}

[CollectionDefinition(Name)]
public sealed class IdentityServer4Collection : ICollectionFixture<IdentityServer4Host>
{
    public const string Name = "IdentityServer4";
}

[CollectionDefinition(Name)]
public sealed class OpenIddictCollection : ICollectionFixture<OpenIddictHost>
{
    public const string Name = "OpenIddict";
}
