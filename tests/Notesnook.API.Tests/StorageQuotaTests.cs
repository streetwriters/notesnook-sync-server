using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Notesnook.API.Models;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public class StorageQuotaTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private static S3Service CreateService(TestDatabase.Scope scope)
    {
        // the quota methods only touch Mongo, so S3 and WAMP are left unconfigured
        Environment.SetEnvironmentVariable("S3_BUCKET_NAME", "test");
        return new((S3Clients)RuntimeHelpers.GetUninitializedObject(typeof(S3Clients)), null!, scope.Repositories, null!, NullLogger<S3Service>.Instance);
    }

    private async Task<string> CreateUserAsync(TestDatabase.Scope scope, long used, DateTimeOffset updatedAt)
    {
        var userId = Guid.NewGuid().ToString("N");
        await scope.Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings
        {
            UserId = userId,
            Salt = "",
            StorageLimit = new Limit { Value = used, UpdatedAt = updatedAt.ToUnixTimeMilliseconds() }
        });
        return userId;
    }

    private static async Task<long> UsedAsync(TestDatabase.Scope scope, string userId) =>
        (await scope.Repositories.UsersSettings.Collection.Find(u => u.UserId == userId).FirstAsync()).StorageLimit!.Value;

    [Fact]
    public async Task Reserve_within_limit_adds_usage()
    {
        var scope = db.CreateScope();
        var userId = await CreateUserAsync(scope, 40, DateTimeOffset.UtcNow);

        Assert.True(await CreateService(scope).TryReserveStorageAsync(userId, 60, 100));
        Assert.Equal(100, await UsedAsync(scope, userId));
    }

    [Fact]
    public async Task Reserve_over_limit_is_refused()
    {
        var scope = db.CreateScope();
        var userId = await CreateUserAsync(scope, 40, DateTimeOffset.UtcNow);

        Assert.False(await CreateService(scope).TryReserveStorageAsync(userId, 61, 100));
        Assert.Equal(40, await UsedAsync(scope, userId));
    }

    [Fact]
    public async Task Unlimited_plans_always_reserve()
    {
        var scope = db.CreateScope();
        var userId = await CreateUserAsync(scope, 40, DateTimeOffset.UtcNow);

        Assert.True(await CreateService(scope).TryReserveStorageAsync(userId, 1_000_000, -1));
        Assert.Equal(1_000_040, await UsedAsync(scope, userId));
    }

    // concurrent uploads must not be able to exceed the quota together
    [Fact]
    public async Task Concurrent_reserves_never_exceed_limit()
    {
        var scope = db.CreateScope();
        var userId = await CreateUserAsync(scope, 0, DateTimeOffset.UtcNow);
        var service = CreateService(scope);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => service.TryReserveStorageAsync(userId, 10, 100))));

        Assert.Equal(10, results.Count(r => r));
        Assert.Equal(100, await UsedAsync(scope, userId));
    }

    [Fact]
    public async Task Last_months_usage_is_rolled_over()
    {
        var scope = db.CreateScope();
        var userId = await CreateUserAsync(scope, 100, DateTimeOffset.UtcNow.AddMonths(-1));

        Assert.True(await CreateService(scope).TryReserveStorageAsync(userId, 30, 100));
        Assert.Equal(30, await UsedAsync(scope, userId));
    }

    [Fact]
    public async Task Release_gives_back_a_reservation()
    {
        var scope = db.CreateScope();
        var userId = await CreateUserAsync(scope, 0, DateTimeOffset.UtcNow);
        var service = CreateService(scope);

        Assert.True(await service.TryReserveStorageAsync(userId, 70, 100));
        await service.ReleaseStorageAsync(userId, 70);

        Assert.Equal(0, await UsedAsync(scope, userId));
    }

    [Fact]
    public async Task Release_never_goes_negative()
    {
        var scope = db.CreateScope();
        var userId = await CreateUserAsync(scope, 10, DateTimeOffset.UtcNow);

        await CreateService(scope).ReleaseStorageAsync(userId, 50);

        Assert.Equal(10, await UsedAsync(scope, userId));
    }
}
