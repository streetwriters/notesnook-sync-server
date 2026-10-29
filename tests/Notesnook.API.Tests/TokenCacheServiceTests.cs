using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Streetwriters.Common;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Services;
using Testcontainers.Redis;

namespace Notesnook.API.Tests;

public sealed class TokenCacheServiceTests : IAsyncLifetime
{
    private readonly RedisContainer redis = new RedisBuilder("redis:8-alpine").Build();

    public Task InitializeAsync() => redis.StartAsync();
    public Task DisposeAsync() => redis.DisposeAsync().AsTask();

    private IDistributedCache Replica() => new RedisCache(Options.Create(new RedisCacheOptions { Configuration = redis.GetConnectionString() }));

    [Fact]
    public async Task Clearing_on_one_replica_clears_every_replica_before_returning()
    {
        var (first, second) = (Replica(), Replica());
        await first.SetStringAsync("token", "introspection");
        await first.SetStringAsync("other", "kept");

        await new TokenCacheService(second).ClearAsync(["token"]);

        Assert.Null(await first.GetStringAsync("token"));
        Assert.Equal("kept", await first.GetStringAsync("other"));
    }

    [Fact]
    public async Task Servers_sharing_the_redis_keep_their_entries_apart()
    {
        Environment.SetEnvironmentVariable("CACHE_REDIS_CONNECTION_STRING", redis.GetConnectionString());
        try
        {
            ServiceProvider Server(Server server) => new ServiceCollection().AddTokenCache(server).BuildServiceProvider();
            using var api = Server(Servers.NotesnookAPI);
            using var subscriptions = Server(Servers.SubscriptionServer);
            await api.GetRequiredService<IDistributedCache>().SetStringAsync("token", "api");
            await subscriptions.GetRequiredService<IDistributedCache>().SetStringAsync("token", "subscriptions");

            await api.GetRequiredService<TokenCacheService>().ClearAsync(["token"]);

            Assert.Null(await api.GetRequiredService<IDistributedCache>().GetStringAsync("token"));
            Assert.Equal("subscriptions", await subscriptions.GetRequiredService<IDistributedCache>().GetStringAsync("token"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CACHE_REDIS_CONNECTION_STRING", null);
        }
    }

    [Fact]
    public async Task A_cache_that_is_down_fails_the_clear()
    {
        var cache = Replica();
        await redis.StopAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => new TokenCacheService(cache).ClearAsync(["token"]));
    }
}
