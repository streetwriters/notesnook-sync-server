using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Notesnook.API.Hubs;
using Notesnook.API.Models;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public sealed class PushNotifierTests
{
    /// <summary>The user's other devices, recording each send as "group-excludedConnection: message".</summary>
    private sealed class Devices : IHubContext<SyncV2Hub, ISyncV2HubClient>, IHubClients<ISyncV2HubClient>
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        /// <summary>Runs before every send with its target; throwing fails it.</summary>
        public Action<string> BeforeSend { get; set; } = (_) => { };

        public IHubClients<ISyncV2HubClient> Clients => this;
        public IGroupManager Groups => throw new NotSupportedException();

        public ISyncV2HubClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            new Proxy(this, $"{groupName}-{string.Join(",", excludedConnectionIds)}");

        private sealed class Proxy(Devices devices, string target) : ISyncV2HubClient
        {
            public Task PushCompleted() => Send("PushCompleted");
            public Task PushCompletedV2(string deviceId) => Send($"PushCompletedV2({deviceId})");

            private Task Send(string message)
            {
                devices.BeforeSend(target);
                devices.Sent.Enqueue($"{target}: {message}");
                return Task.CompletedTask;
            }

            public Task<bool> SendItems(SyncTransferRawItemV2 transferItem) => throw new NotSupportedException();
            public Task<bool> SendVaultKey(EncryptedData vaultKey) => throw new NotSupportedException();
            public Task<bool> SendMonographs(IEnumerable<MonographMetadata> monographs) => throw new NotSupportedException();
            public Task<bool> SendInboxItems(IEnumerable<InboxSyncItem> inboxItems) => throw new NotSupportedException();
        }

        ISyncV2HubClient IHubClients<ISyncV2HubClient>.All => throw new NotSupportedException();
        ISyncV2HubClient IHubClients<ISyncV2HubClient>.AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        ISyncV2HubClient IHubClients<ISyncV2HubClient>.Client(string connectionId) => throw new NotSupportedException();
        ISyncV2HubClient IHubClients<ISyncV2HubClient>.Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        ISyncV2HubClient IHubClients<ISyncV2HubClient>.Group(string groupName) => throw new NotSupportedException();
        ISyncV2HubClient IHubClients<ISyncV2HubClient>.Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        ISyncV2HubClient IHubClients<ISyncV2HubClient>.User(string userId) => throw new NotSupportedException();
        ISyncV2HubClient IHubClients<ISyncV2HubClient>.Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class Logs : ILogger<PushNotifier>
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Enqueue(logLevel);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition());
    }

    private static async Task<string[]> SentAfterAsync(Devices devices, PushNotifier notifier, int count)
    {
        await notifier.StartAsync(CancellationToken.None);
        await UntilAsync(() => devices.Sent.Count >= count);
        await Task.Delay(100);
        await notifier.StopAsync(CancellationToken.None);
        return [.. devices.Sent.Order()];
    }

    [Fact]
    public async Task It_tells_the_users_other_devices()
    {
        var devices = new Devices();
        using var notifier = new PushNotifier(devices, new Logs());
        notifier.Notify("user", "pusher", "device");
        notifier.Notify("user", "old-client", deviceId: null);

        var sent = await SentAfterAsync(devices, notifier, 3);

        Assert.Equal(["user-old-client: PushCompleted", "user-pusher: PushCompleted", "user-pusher: PushCompletedV2(device)"], sent);
    }

    [Fact]
    public async Task A_connections_pushes_waiting_to_be_announced_become_one()
    {
        var devices = new Devices();
        using var notifier = new PushNotifier(devices, new Logs());
        for (var i = 0; i < 10; i++) notifier.Notify("user", "pusher", "device");

        var sent = await SentAfterAsync(devices, notifier, 2);

        Assert.Equal(["user-pusher: PushCompleted", "user-pusher: PushCompletedV2(device)"], sent);
    }

    [Fact]
    public async Task A_PushCompletedV2_waiting_behind_a_PushCompleted_is_still_sent()
    {
        var devices = new Devices();
        using var notifier = new PushNotifier(devices, new Logs());
        notifier.Notify("user", "pusher", deviceId: null);
        notifier.Notify("user", "pusher", "device");

        var sent = await SentAfterAsync(devices, notifier, 2);

        Assert.Equal(["user-pusher: PushCompleted", "user-pusher: PushCompletedV2(device)"], sent);
    }

    [Fact]
    public async Task Each_of_a_users_pushing_devices_tells_the_others()
    {
        var devices = new Devices();
        using var notifier = new PushNotifier(devices, new Logs());
        notifier.Notify("user", "phone", "phone");
        notifier.Notify("user", "laptop", "laptop");
        notifier.Notify("user", "phone", "phone");

        var sent = await SentAfterAsync(devices, notifier, 4);

        Assert.Equal(["user-laptop: PushCompleted", "user-laptop: PushCompletedV2(laptop)", "user-phone: PushCompleted", "user-phone: PushCompletedV2(phone)"], sent);
    }

    [Fact]
    public async Task A_push_made_while_its_last_one_is_being_announced_is_announced_again()
    {
        var devices = new Devices();
        using var notifier = new PushNotifier(devices, new Logs());
        using var sending = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        devices.BeforeSend = (_) =>
        {
            if (sending.IsSet) return;
            sending.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };
        await notifier.StartAsync(CancellationToken.None);

        notifier.Notify("user", "pusher", "first");
        Assert.True(sending.Wait(TimeSpan.FromSeconds(10)));
        notifier.Notify("user", "pusher", "second");
        release.Set();
        await UntilAsync(() => devices.Sent.Count == 4);
        await notifier.StopAsync(CancellationToken.None);

        Assert.Contains("user-pusher: PushCompletedV2(second)", devices.Sent);
    }

    [Fact]
    public async Task Failing_sends_are_logged_once_and_later_ones_still_go_out()
    {
        var devices = new Devices();
        var logs = new Logs();
        using var notifier = new PushNotifier(devices, logs);
        var down = true;
        devices.BeforeSend = (_) => { if (down) throw new InvalidOperationException("Redis is down"); };
        await notifier.StartAsync(CancellationToken.None);

        for (var i = 0; i < 6; i++) notifier.Notify("user", $"down-{i}", "device");
        await UntilAsync(() => logs.Levels.Count >= 1);
        await Task.Delay(100);
        down = false;
        notifier.Notify("user", "up", "device");
        await UntilAsync(() => devices.Sent.Contains("user-up: PushCompletedV2(device)"));
        await notifier.StopAsync(CancellationToken.None);

        Assert.Equal([LogLevel.Warning, LogLevel.Information], logs.Levels);
    }

    [Fact]
    public async Task A_stuck_send_doesnt_hold_up_the_others()
    {
        var devices = new Devices();
        using var notifier = new PushNotifier(devices, new Logs());
        using var release = new ManualResetEventSlim();
        devices.BeforeSend = (target) => { if (target == "user-stuck") release.Wait(TimeSpan.FromSeconds(30)); };
        await notifier.StartAsync(CancellationToken.None);

        notifier.Notify("user", "stuck", "device");
        for (var i = 0; i < 20; i++) notifier.Notify("user", $"pusher-{i}", "device");
        await UntilAsync(() => devices.Sent.Count(m => m.StartsWith("user-pusher-")) == 40);
        Assert.DoesNotContain(devices.Sent, (m) => m.StartsWith("user-stuck"));

        release.Set();
        await UntilAsync(() => devices.Sent.Count == 42);
        await notifier.StopAsync(CancellationToken.None);
    }
}
