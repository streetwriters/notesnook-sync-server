using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Jobs;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Quartz;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using WampSharp.V2;

namespace Notesnook.API.Tests;

public class UserPurgeJobTests : IClassFixture<TestDatabase>, IDisposable
{
    private readonly TestDatabase db;
    private readonly DefaultWampHost subscriptionServer;
    private readonly WampServer subscriptionClient;
    private readonly ConcurrentQueue<string> deletedSubscriptions = new();

    public UserPurgeJobTests(TestDatabase db)
    {
        this.db = db;
        // the client derives its auth token from this; the test router doesn't check it
        Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET", "test-secret");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        subscriptionServer = new DefaultWampHost($"ws://127.0.0.1:{port}/wamp");
        subscriptionServer.RealmContainer.GetRealmByName("messages").Services
            .GetSubject<DeleteSubscriptionMessage>(SubscriptionServerTopics.DeleteSubscriptionTopic)
            .Subscribe((m) => deletedSubscriptions.Enqueue(m.UserId), (_) => { });
        subscriptionServer.Open();
        subscriptionClient = new WampServer { Endpoint = "/wamp", Port = port, Address = $"ws://127.0.0.1:{port}/wamp", Realm = "messages" };
    }

    public void Dispose() => subscriptionServer.Dispose();

    private static string NewUserId() => Guid.NewGuid().ToString("N");
    private static readonly long LongAgo = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeMilliseconds();

    private static IJobExecutionContext Context => Stub<IJobExecutionContext>.Create(("get_CancellationToken", (_) => CancellationToken.None));

    private static IUserAccountService Identity(Func<string, bool> exists) =>
        Stub<IUserAccountService>.Create(("UserExistsAsync", (args) => Task.FromResult(exists((string)args[0]!))));

    private static IS3Service S3(Func<string, Task>? deleteDirectory = null) =>
        Stub<IS3Service>.Create(("DeleteDirectoryAsync", (args) => deleteDirectory?.Invoke((string)args[0]!) ?? Task.CompletedTask));

    private Task RunAsync(IUserAccountService identity, IS3Service s3)
    {
        var scope = db.CreateScope();
        var accessor = new WampServiceAccessor(Servers.NotesnookAPI) { UserAccountService = identity };
        return new UserPurgeJob(scope.Repositories, scope.Devices, scope.ResetPurge, s3, accessor, NullLogger<UserPurgeJob>.Instance) { SubscriptionServer = subscriptionClient }.Execute(Context);
    }

    private async Task CreateUserAsync(string userId, long? deletedAt)
    {
        var scope = db.CreateScope();
        await scope.Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings { UserId = userId, Salt = "salt", DeletedAt = deletedAt });
        await scope.Repositories.Notes.Collection.InsertOneAsync(new SyncItem { UserId = userId, ItemId = "note" });
        await scope.Devices.RegisterDeviceAsync(userId, "device");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(100, cts.Token);
    }

    private Task<UserSettings?> SettingsAsync(string userId) =>
        db.CreateScope().Repositories.UsersSettings.Collection.Find((s) => s.UserId == userId).FirstOrDefaultAsync()!;

    private Task<long> CountNotesAsync(string userId) =>
        db.CreateScope().Repositories.Notes.Collection.CountDocumentsAsync((i) => i.UserId == userId);

    private Task<long> CountDevicesAsync(string userId) =>
        db.CreateScope().Repositories.SyncDevices.Collection.CountDocumentsAsync((d) => d.UserId == userId);

    [Fact]
    public async Task Purges_a_deleted_user_but_not_another_user()
    {
        var (deleted, other) = (NewUserId(), NewUserId());
        await CreateUserAsync(deleted, LongAgo);
        await CreateUserAsync(other, null);
        var purgedDirectories = new ConcurrentQueue<string>();

        await RunAsync(Identity((id) => id == other), S3((id) => { purgedDirectories.Enqueue(id); return Task.CompletedTask; }));

        Assert.Null(await SettingsAsync(deleted));
        Assert.Equal(0, await CountNotesAsync(deleted));
        Assert.Equal(0, await CountDevicesAsync(deleted));
        Assert.Contains(deleted, purgedDirectories);
        Assert.DoesNotContain(other, purgedDirectories);
        await WaitUntilAsync(() => deletedSubscriptions.Contains(deleted));
        Assert.DoesNotContain(other, deletedSubscriptions);
        Assert.NotNull(await SettingsAsync(other));
        Assert.Equal(1, await CountNotesAsync(other));
        Assert.Equal(1, await CountDevicesAsync(other));
    }

    [Fact]
    public async Task A_user_that_still_exists_keeps_its_data_and_loses_the_mark()
    {
        var userId = NewUserId();
        await CreateUserAsync(userId, LongAgo);

        await RunAsync(Identity((_) => true), S3());

        Assert.Null((await SettingsAsync(userId))!.DeletedAt);
        Assert.Equal(1, await CountNotesAsync(userId));
    }

    [Fact]
    public async Task A_delete_retried_during_the_existence_check_keeps_its_mark()
    {
        var userId = NewUserId();
        await CreateUserAsync(userId, LongAgo);
        var retriedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        async Task<bool> RetryThenExist()
        {
            await db.CreateScope().Repositories.UsersSettings.Collection.UpdateOneAsync((s) => s.UserId == userId, Builders<UserSettings>.Update.Set((s) => s.DeletedAt, retriedAt));
            return true;
        }

        await RunAsync(Stub<IUserAccountService>.Create(("UserExistsAsync", (_) => RetryThenExist())), S3());

        Assert.Equal(retriedAt, (await SettingsAsync(userId))!.DeletedAt);
    }

    [Fact]
    public async Task A_user_deleted_within_the_grace_period_is_left_alone()
    {
        var userId = NewUserId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await CreateUserAsync(userId, now);

        await RunAsync(Identity((_) => false), S3());

        Assert.Equal(now, (await SettingsAsync(userId))!.DeletedAt);
        Assert.Equal(1, await CountNotesAsync(userId));
    }

    [Fact]
    public async Task A_failed_purge_keeps_the_mark_and_the_next_run_finishes_it()
    {
        var userId = NewUserId();
        await CreateUserAsync(userId, LongAgo);

        await RunAsync(Identity((_) => false), S3((_) => throw new Exception("S3 is down")));

        Assert.Equal(LongAgo, (await SettingsAsync(userId))!.DeletedAt);

        await RunAsync(Identity((_) => false), S3());

        Assert.Null(await SettingsAsync(userId));
        Assert.Equal(0, await CountNotesAsync(userId));
    }

    [Fact]
    public async Task An_unreachable_identity_leaves_the_user_untouched()
    {
        var userId = NewUserId();
        await CreateUserAsync(userId, LongAgo);

        await RunAsync(Stub<IUserAccountService>.Create(("UserExistsAsync", (_) => Task.FromException<bool>(new TimeoutException()))), S3());

        Assert.Equal(LongAgo, (await SettingsAsync(userId))!.DeletedAt);
        Assert.Equal(1, await CountNotesAsync(userId));
    }
}
