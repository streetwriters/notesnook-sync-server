using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Services;
using StackExchange.Redis;
using Streetwriters.Data.Interfaces;
using Testcontainers.Redis;

namespace Notesnook.API.Tests;

/// <summary>The sync hub on the production Redis backplane, while Redis is down and after it comes back.</summary>
/// <remarks>Stopped, not paused: with a frozen connection SignalR's synchronous UNSUBSCRIBE on shutdown fails the host's disposal.</remarks>
public sealed class SyncV2HubRedisOutageTests(TestDatabase db) : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private const string UserHeader = "X-Test-User";
    // fixed, so the restarted container keeps the address the server connects to
    private static readonly int RedisPort = FreePort();
    private readonly RedisContainer redis = new RedisBuilder("redis:8-alpine").WithPortBinding(RedisPort, 6379).Build();

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public Task InitializeAsync() => redis.StartAsync();
    public Task DisposeAsync() => redis.DisposeAsync().AsTask();

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("scope", "notesnook.sync"),
                new Claim("aud", "notesnook"),
                new Claim("role", "notesnook"),
                new Claim("verified", "true")
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private async Task<WebApplication> StartServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR().AddJsonProtocol()
            .AddHubOptions<SyncV2Hub>((hub) => hub.AddFilter<SyncAuthorizationHubFilter>())
            .AddStackExchangeRedis((options) =>
            {
                options.Configuration = ConfigurationOptions.Parse(redis.GetConnectionString());
                options.Configuration.AbortOnConnectFail = false;
                options.Configuration.ConnectRetry = 5;
                options.Configuration.ReconnectRetryPolicy = new ExponentialRetry(5000, 30000);
                options.Configuration.KeepAlive = 60;
                options.Configuration.ConnectTimeout = 5000;
                options.Configuration.SyncTimeout = 5000;
            });
        builder.Services.AddSingleton<SyncAuthorizationHubFilter>();
        builder.Services.AddSingleton<PushNotifier>();
        builder.Services.AddHostedService((provider) => provider.GetRequiredService<PushNotifier>());
        builder.Services.AddScoped((_) => db.CreateScope());
        builder.Services.AddScoped<ISyncItemsRepositoryAccessor>((sp) => sp.GetRequiredService<TestDatabase.Scope>().Repositories);
        builder.Services.AddScoped<IUnitOfWork>((sp) => sp.GetRequiredService<TestDatabase.Scope>().Unit);
        builder.Services.AddScoped((sp) => sp.GetRequiredService<TestDatabase.Scope>().Devices);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<SyncV2Hub>("/hubs/sync/v2", (options) => options.Transports = HttpTransportType.WebSockets);
        await app.StartAsync();
        return app;
    }

    private static HubConnection Connect(WebApplication app, string userId) => new HubConnectionBuilder()
        .WithUrl(app.Urls.First() + "/hubs/sync/v2", (options) =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
            options.Headers[UserHeader] = userId;
        })
        .AddJsonProtocol()
        .Build();

    private async Task<string> AddUserAsync()
    {
        var userId = Guid.NewGuid().ToString("N");
        await db.CreateScope().Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings { UserId = userId, Salt = "salt" });
        return userId;
    }

    private static async Task NotifiedAfterRecoveryAsync(HubConnection from, TaskCompletionSource<string> to, string device)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!to.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            // a group join (SUBSCRIBE) in flight as Redis reconnects rejects the connection, so connect again like clients do
            if (from.State == HubConnectionState.Disconnected)
            {
                try { await from.StartAsync(); }
                catch (Exception) { }
            }
            try { await from.InvokeAsync<bool>("PushCompletedV2", device); }
            catch (InvalidOperationException) when (from.State != HubConnectionState.Connected) { }
            await Task.WhenAny(to.Task, Task.Delay(1000));
        }
        Assert.Equal(device, await to.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task A_push_completes_and_devices_connect_while_redis_is_down()
    {
        var userId = await AddUserAsync();
        await using var app = await StartServerAsync();
        await using var first = Connect(app, userId);
        await using var second = Connect(app, userId);
        var firstNotified = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.On<string>("PushCompletedV2", (device) => { if (device == "second-after") firstNotified.TrySetResult(device); });
        await first.StartAsync();
        await second.StartAsync();
        // StartAsync returns before OnConnectedAsync joins the group; an answered call means it has
        await first.InvokeAsync<bool>("PushCompletedV2", "first");
        await second.InvokeAsync<bool>("PushCompletedV2", "second-before");

        await redis.StopAsync();
        var started = DateTime.UtcNow;
        Assert.True(await second.InvokeAsync<bool>("PushCompletedV2", "second"));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));
        // a user with no connection yet, so its group has no Redis subscription to reuse
        var freshUser = await AddUserAsync();
        await using var fresh = Connect(app, freshUser);
        var freshNotified = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        fresh.On<string>("PushCompletedV2", (device) => freshNotified.TrySetResult(device));
        await fresh.StartAsync();

        await redis.StartAsync();
        await NotifiedAfterRecoveryAsync(second, firstNotified, "second-after");
        await using var freshSecond = Connect(app, freshUser);
        await freshSecond.StartAsync();
        await NotifiedAfterRecoveryAsync(freshSecond, freshNotified, "fresh-second");
    }
}
