using System.Buffers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Tests;

/// <summary>
/// Runs the sync hub on a real Kestrel server with a real SignalR client over WebSockets, to check
/// what the server keeps alive while a device is still applying a chunk.
/// </summary>
public class SyncV2HubSignalRTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private const string UserHeader = "X-Test-User";
    private const string VerifiedHeader = "X-Test-Verified";

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
                new Claim("verified", Request.Headers.TryGetValue(VerifiedHeader, out var verified) ? verified.ToString() : "true")
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    /// <summary>
    /// The JSON protocol the server uses, recording a weak reference to every item it writes to a client.
    /// </summary>
    private sealed class TrackingProtocol(IHubProtocol inner) : IHubProtocol
    {
        // one entry per SendItems message: the item list and every item in it
        private readonly List<List<WeakReference>> chunks = [];

        public string Name => inner.Name;
        public int Version => inner.Version;
        public TransferFormat TransferFormat => inner.TransferFormat;
        public bool IsVersionSupported(int version) => inner.IsVersionSupported(version);
        public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, out HubMessage? message) => inner.TryParseMessage(ref input, binder, out message);

        public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
        {
            Track(message);
            inner.WriteMessage(message, output);
        }

        public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message)
        {
            Track(message);
            return inner.GetMessageBytes(message);
        }

        private void Track(HubMessage message)
        {
            if (message is not InvocationMessage { Target: "SendItems" } invocation) return;
            var transfer = (SyncTransferRawItemV2)invocation.Arguments[0]!;
            lock (chunks) chunks.Add([new WeakReference(transfer.Items), .. transfer.Items.Select((item) => new WeakReference(item))]);
        }

        public int[] CountAlivePerChunk()
        {
            lock (chunks) return [.. chunks.Select((c) => c.Count((w) => w.IsAlive))];
        }
    }

    private async Task<(WebApplication App, TrackingProtocol Protocol)> StartServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR().AddJsonProtocol()
            .AddHubOptions<SyncV2Hub>((hub) => hub.AddFilter<SyncAuthorizationHubFilter>());
        builder.Services.AddSingleton<SyncAuthorizationHubFilter>();
        var inner = builder.Services.BuildServiceProvider().GetServices<IHubProtocol>().Single();
        var protocol = new TrackingProtocol(inner);
        builder.Services.RemoveAll<IHubProtocol>();
        builder.Services.AddSingleton<IHubProtocol>(protocol);
        builder.Services.AddScoped((_) => db.CreateScope());
        builder.Services.AddScoped<ISyncItemsRepositoryAccessor>((sp) => sp.GetRequiredService<TestDatabase.Scope>().Repositories);
        builder.Services.AddScoped<IUnitOfWork>((sp) => sp.GetRequiredService<TestDatabase.Scope>().Unit);
        builder.Services.AddScoped((sp) => sp.GetRequiredService<TestDatabase.Scope>().Devices);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<SyncV2Hub>("/hubs/sync/v2", (options) => options.Transports = HttpTransportType.WebSockets);
        await app.StartAsync();
        return (app, protocol);
    }

    private static HubConnection Connect(WebApplication app, string userId, bool verified = true)
    {
        var builder = new HubConnectionBuilder()
        .WithUrl(app.Urls.First() + "/hubs/sync/v2", (options) =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
            options.Headers[UserHeader] = userId;
            options.Headers[VerifiedHeader] = verified ? "true" : "false";
        });
        // the .NET client reads items into SyncItem, so this also checks what clients get from the stored bytes
        return builder.AddJsonProtocol().Build();
    }

    private static void CollectGarbage()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    [Fact]
    public async Task The_server_keeps_only_the_chunk_awaiting_an_ack_alive()
    {
        var userId = Guid.NewGuid().ToString("N");
        await db.CreateScope().Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings { UserId = userId, Salt = "salt" });
        // 1MB items against the 3MB budget: chunks of 3, 3 and 1, so the second one is awaited mid-cursor
        await db.CreateScope().Repositories.Notes.Collection.InsertManyAsync(Enumerable.Range(0, 7).Select((_) => new SyncItem
        {
            UserId = userId,
            ItemId = Guid.NewGuid().ToString("N"),
            Cipher = new string('a', 1024 * 1024),
            IV = "iv",
            Length = 1024 * 1024,
            Version = 5.9,
            Algorithm = Algorithms.Default
        }));

        var (app, protocol) = await StartServerAsync();
        await using var _ = app;
        await using var connection = Connect(app, userId);

        var received = new List<string>();
        var ciphersIntact = true;
        var secondChunk = new TaskCompletionSource();
        var applied = new TaskCompletionSource();
        connection.On<SyncTransferItemV2, bool>("SendItems", async (transfer) =>
        {
            int count;
            lock (received)
            {
                received.AddRange(transfer.Items.Select((i) => i.ItemId!));
                ciphersIntact &= transfer.Items.All((i) => i.Cipher.Length == 1024 * 1024 && i.Algorithm == Algorithms.Default && i.Version == 5.9);
                count = received.Count;
            }
            if (count != 6) return true;
            secondChunk.TrySetResult();
            // a slow device: the server waits for this ack
            await applied.Task;
            return true;
        });
        await connection.StartAsync();

        var fetch = connection.InvokeAsync<SyncV2Metadata>("RequestFetchV4", "new-device");
        await secondChunk.Task.WaitAsync(TimeSpan.FromSeconds(30));

        CollectGarbage();
        var alive = protocol.CountAlivePerChunk();

        applied.SetResult();
        var result = await fetch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, alive.Length);
        // the acked chunk is gone; SignalR itself holds the arguments of the invocation still awaiting its result
        Assert.Equal(0, alive[0]);
        Assert.True(result.Synced);
        Assert.Equal(7, received.Distinct().Count());
        Assert.True(ciphersIntact);
    }

    [Fact]
    public async Task An_unverified_user_is_rejected_with_a_message_clients_can_read()
    {
        var (app, _) = await StartServerAsync();
        await using var _app = app;
        await using var connection = Connect(app, Guid.NewGuid().ToString("N"), verified: false);

        var closed = new TaskCompletionSource<Exception?>();
        connection.Closed += (error) => { closed.TrySetResult(error); return Task.CompletedTask; };
        // the rejection can surface from StartAsync or as the close error
        try { await connection.StartAsync(); } catch (Exception ex) { closed.TrySetResult(ex); }
        var error = await closed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // packages/core/src/api/sync/index.ts matches on this
        Assert.Contains("HubException: Please confirm your email", error?.Message);
    }
}
