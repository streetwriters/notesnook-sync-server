using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Streetwriters.Common.Services;

namespace Notesnook.API.Tests;

public sealed class FailOpenDistributedCacheTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Now.UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    /// <summary>A cache that throws what StackExchange.Redis throws while down or slow, counting the calls that reach it.</summary>
    private sealed class Redis : IDistributedCache, IDisposable
    {
        private readonly Dictionary<string, byte[]> entries = [];
        private int calls;
        public bool Down { get; set; }
        public bool Slow { get; set; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource? Hold { get; set; }
        public int Calls => Volatile.Read(ref calls);

        private void Reach()
        {
            Interlocked.Increment(ref calls);
            if (Down) throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connection is available");
            if (Slow) throw new RedisTimeoutException("Timeout performing GET", CommandStatus.Sent);
        }

        public byte[]? Get(string key)
        {
            Reach();
            lock (entries) return entries.GetValueOrDefault(key);
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            var hold = Hold;
            Interlocked.Increment(ref calls);
            if (hold != null) await hold.Task;
            Interlocked.Decrement(ref calls);
            return Get(key);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Reach();
            lock (entries) entries[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Set(key, value, options); return Task.CompletedTask; }
        public void Refresh(string key) => Reach();
        public Task RefreshAsync(string key, CancellationToken token = default) { Reach(); return Task.CompletedTask; }

        public void Remove(string key)
        {
            Reach();
            lock (entries) entries.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Logs : ILogger<FailOpenDistributedCache>
    {
        private int warnings;
        public int Warnings => Volatile.Read(ref warnings);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Interlocked.Increment(ref warnings);
        }
    }

    private static (FailOpenDistributedCache Cache, Redis Redis, Clock Clock, Logs Logs) Create()
    {
        var (redis, clock, logs) = (new Redis(), new Clock(), new Logs());
        return (new FailOpenDistributedCache(redis, logs, clock), redis, clock, logs);
    }

    private static readonly DistributedCacheEntryOptions Forever = new();

    [Fact]
    public async Task While_redis_is_down_reads_miss_and_writes_are_dropped()
    {
        var (cache, redis, _, _) = Create();
        await cache.SetAsync("token", [1], Forever);
        redis.Down = true;

        Assert.Null(await cache.GetAsync("token"));
        await cache.SetAsync("other", [2], Forever);
        await cache.RefreshAsync("token");
    }

    [Fact]
    public async Task A_lost_connection_skips_redis_until_one_call_tries_it_again()
    {
        var (cache, redis, clock, logs) = Create();
        await cache.SetAsync("token", [1], Forever);
        redis.Down = true;
        await cache.GetAsync("token");
        var calls = redis.Calls;

        for (var i = 0; i < 5; i++) Assert.Null(await cache.GetAsync("token"));
        Assert.Equal(calls, redis.Calls);

        clock.Now += FailOpenDistributedCache.BreakDuration;
        Assert.Null(await cache.GetAsync("token"));
        Assert.Null(await cache.GetAsync("token"));
        Assert.Equal(calls + 1, redis.Calls);

        redis.Down = false;
        clock.Now += FailOpenDistributedCache.BreakDuration;
        Assert.Equal([1], await cache.GetAsync("token"));
        Assert.Equal([1], await cache.GetAsync("token"));
        Assert.Equal(calls + 3, redis.Calls);
        Assert.Equal(1, logs.Warnings);
    }

    [Fact]
    public async Task Timeouts_skip_redis_only_once_several_come_in_a_row()
    {
        var (cache, redis, _, _) = Create();
        redis.Slow = true;
        for (var i = 1; i < FailOpenDistributedCache.TimeoutsToBreak; i++) await cache.GetAsync("token");
        redis.Slow = false;
        await cache.GetAsync("token");
        redis.Slow = true;
        for (var i = 1; i < FailOpenDistributedCache.TimeoutsToBreak; i++) await cache.GetAsync("token");
        var calls = redis.Calls;

        await cache.GetAsync("token");
        Assert.Equal(calls + 1, redis.Calls);
        await cache.GetAsync("token");
        await cache.GetAsync("token");
        Assert.Equal(calls + 1, redis.Calls);
    }

    [Fact]
    public async Task A_read_that_started_before_the_break_doesnt_end_it()
    {
        var (cache, redis, _, _) = Create();
        var hold = new TaskCompletionSource();
        redis.Hold = hold;
        var early = cache.GetAsync("token");
        redis.Hold = null;
        redis.Down = true;
        await cache.GetAsync("token");

        redis.Down = false;
        hold.SetResult();
        await early;
        var calls = redis.Calls;
        await cache.GetAsync("token");
        Assert.Equal(calls, redis.Calls);
    }

    [Fact]
    public async Task When_the_break_ends_only_one_of_many_concurrent_reads_tries_redis()
    {
        var (cache, redis, clock, _) = Create();
        redis.Down = true;
        await cache.GetAsync("token");
        redis.Down = false;
        clock.Now += FailOpenDistributedCache.BreakDuration;
        var hold = new TaskCompletionSource();
        redis.Hold = hold;
        var calls = redis.Calls;

        var reads = Enumerable.Range(0, 20).Select((_) => Task.Run(() => cache.GetAsync("token"))).ToArray();
        await Task.Delay(100);
        Assert.Equal(calls + 1, redis.Calls);
        hold.SetResult();
        await Task.WhenAll(reads);
    }

    [Fact]
    public async Task Removing_fails_while_redis_is_down_even_during_a_break()
    {
        var (cache, redis, _, _) = Create();
        redis.Down = true;
        await cache.GetAsync("token");

        await Assert.ThrowsAsync<RedisConnectionException>(() => cache.RemoveAsync("token"));
        Assert.Throws<RedisConnectionException>(() => cache.Remove("token"));
    }

    [Fact]
    public void Disposing_it_disposes_the_redis_cache()
    {
        var (cache, redis, _, _) = Create();
        cache.Dispose();
        Assert.True(redis.Disposed);
    }
}
