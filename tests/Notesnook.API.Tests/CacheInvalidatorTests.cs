using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Streetwriters.Common;
using Streetwriters.Common.Services;
using Testcontainers.Redis;

namespace Notesnook.API.Tests;

// both tests set CACHE_REDIS_CONNECTION_STRING, so they live in one class to run sequentially
public sealed class CacheInvalidatorTests : IAsyncLifetime
{
    private readonly RedisContainer redis = new RedisBuilder("redis:8-alpine").Build();
    private static readonly Server Server = new() { Id = "test", Hostname = "localhost" };

    public Task InitializeAsync() => redis.StartAsync();
    public Task DisposeAsync() => redis.DisposeAsync().AsTask();

    private static (CacheInvalidator, IDistributedCache) Replica()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return (new CacheInvalidator(Server, cache, NullLogger<CacheInvalidator>.Instance), cache);
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await Task.Delay(100);
        return condition();
    }

    [Fact]
    public async Task ClearingOnOneReplicaClearsEveryReplica()
    {
        Environment.SetEnvironmentVariable("CACHE_REDIS_CONNECTION_STRING", redis.GetConnectionString());
        var (first, firstCache) = Replica();
        var (second, secondCache) = Replica();
        await first.StartAsync(default);
        await second.StartAsync(default);
        try
        {
            firstCache.SetString("token", "introspection");
            secondCache.SetString("token", "introspection");
            firstCache.SetString("other", "kept");

            await first.ClearAsync(["token"]);

            Assert.True(await EventuallyAsync(() => firstCache.GetString("token") == null && secondCache.GetString("token") == null));
            Assert.Equal("kept", firstCache.GetString("other"));
        }
        finally
        {
            await first.StopAsync(default);
            await second.StopAsync(default);
        }
    }

    [Fact]
    public async Task WithoutRedisClearsOnlyThisInstance()
    {
        Environment.SetEnvironmentVariable("CACHE_REDIS_CONNECTION_STRING", null);
        var (invalidator, cache) = Replica();
        await invalidator.StartAsync(default);
        cache.SetString("token", "introspection");

        await invalidator.ClearAsync(["token"]);

        Assert.Null(cache.GetString("token"));
    }
}
