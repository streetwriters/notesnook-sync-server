using System.Security.Claims;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Streetwriters.Messenger.Helpers;

namespace Streetwriters.Messenger.Tests;

public class SSEHelperTests
{
    // #27: events reach only the target user's clients, minus the token that caused them
    [Fact]
    public async Task User_events_go_to_that_users_other_clients()
    {
        var (service, fake) = FakeService.Create();
        var origin = new FakeClient("alice", "t1");
        var other = new FakeClient("alice", "t2");
        var stranger = new FakeClient("bob");
        fake.Clients.AddRange([origin, other, stranger]);

        await SSEHelper.SendEventToUserAsync("hello", service, "alice", "t1");

        Assert.Empty(origin.Received);
        Assert.Equal(["hello"], other.Received);
        Assert.Empty(stranger.Received);
    }

    [Fact]
    public async Task Disconnected_clients_are_skipped()
    {
        var (service, fake) = FakeService.Create();
        var gone = new FakeClient("alice") { IsConnected = false };
        var live = new FakeClient("bob");
        fake.Clients.AddRange([gone, live]);

        await SSEHelper.SendEventToAllUsersAsync("hello", service);

        Assert.Empty(gone.Received);
        Assert.Equal(["hello"], live.Received);
    }

    // #27: a slow client must not hold up the others
    [Fact]
    public async Task Clients_are_sent_to_concurrently()
    {
        var (service, fake) = FakeService.Create();
        var slow = new FakeClient("alice") { Gate = new() };
        var fast = new FakeClient("bob");
        fake.Clients.AddRange([slow, fast]);

        var send = SSEHelper.SendEventToAllUsersAsync("hello", service);

        Assert.Equal(["hello"], fast.Received);
        Assert.False(send.IsCompleted);
        slow.Gate.SetResult();
        await send;
        Assert.Equal(["hello"], slow.Received);
    }

    // #27: a stuck client is given up on instead of stalling the heartbeat forever
    [Fact]
    public async Task Stuck_clients_time_out()
    {
        var (service, fake) = FakeService.Create();
        var stuck = new FakeClient("alice") { Gate = new() };
        fake.Clients.Add(stuck);

        await SSEHelper.SendEventToAllUsersAsync("hello", service).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(stuck.Received);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var (service, fake) = FakeService.Create();
        fake.Clients.Add(new FakeClient("alice") { Gate = new() });
        using var cts = new CancellationTokenSource();

        var send = SSEHelper.SendEventToAllUsersAsync("hello", service, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    // #27: against the real library, connected clients join their user's group and leave it on disconnect
    [Fact]
    public async Task Clients_join_and_leave_their_user_group()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddServerSentEvents(options =>
                {
                    options.OnClientConnected = (s, args) => SSEHelper.AddToUserGroup(s, args.Client);
                }))
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", context.Request.Query["user"].ToString())], "test"));
                        return next();
                    });
                    app.MapServerSentEvents("/sse");
                }))
            .StartAsync();
        var service = host.Services.GetRequiredService<IServerSentEventsService>();
        var http = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/sse?user=alice");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        await WaitUntil(() => service.GetClients("alice").Count == 1);
        Assert.Empty(service.GetClients("bob"));

        // disposing the response aborts the request, which disconnects the client
        response.Dispose();
        await WaitUntil(() => service.GetClients("alice").Count == 0);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
