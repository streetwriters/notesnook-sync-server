/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Streetwriters.Common.Services
{
    /// <summary>
    /// The token cache, which keeps working while Redis is down: reads miss (so tokens are introspected) and writes are dropped.
    /// Removing still throws, since Identity counts a revocation done only once every server confirms it.
    /// </summary>
    public sealed class FailOpenDistributedCache(IDistributedCache inner, ILogger<FailOpenDistributedCache> logger, TimeProvider timeProvider) : IDistributedCache, IDisposable
    {
        public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(5);
        public const int TimeoutsToBreak = 3;

        private enum Attempt { Normal, Probe, Skip }

        // 0 while Redis is up
        private long skipUntil;
        private int timeoutsInARow;

        public byte[]? Get(string key) => Try(() => inner.Get(key), null);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => TryAsync(() => inner.GetAsync(key, token), null);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Try(() => { inner.Set(key, value, options); return true; }, false);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            TryAsync(async () => { await inner.SetAsync(key, value, options, token); return true; }, false);
        public void Refresh(string key) => Try(() => { inner.Refresh(key); return true; }, false);
        public Task RefreshAsync(string key, CancellationToken token = default) => TryAsync(async () => { await inner.RefreshAsync(key, token); return true; }, false);
        public void Remove(string key) => inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);

        public void Dispose() => (inner as IDisposable)?.Dispose();

        private T Try<T>(Func<T> call, T fallback)
        {
            var attempt = Begin();
            if (attempt == Attempt.Skip) return fallback;
            try
            {
                var result = call();
                Succeeded(attempt);
                return result;
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                Failed(attempt, ex);
                return fallback;
            }
        }

        private async Task<T> TryAsync<T>(Func<Task<T>> call, T fallback)
        {
            var attempt = Begin();
            if (attempt == Attempt.Skip) return fallback;
            try
            {
                var result = await call();
                Succeeded(attempt);
                return result;
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                Failed(attempt, ex);
                return fallback;
            }
        }

        private Attempt Begin()
        {
            var until = Interlocked.Read(ref skipUntil);
            if (until == 0) return Attempt.Normal;
            var now = timeProvider.GetTimestamp();
            if (now < until) return Attempt.Skip;
            // only the call that moves the deadline probes Redis
            return Interlocked.CompareExchange(ref skipUntil, Deadline(now), until) == until ? Attempt.Probe : Attempt.Skip;
        }

        private void Succeeded(Attempt attempt)
        {
            if (attempt == Attempt.Probe)
            {
                Interlocked.Exchange(ref timeoutsInARow, 0);
                Interlocked.Exchange(ref skipUntil, 0);
                logger.LogInformation("The token cache is reachable again");
            }
            // a call that started before the break proves nothing about Redis now
            else if (Volatile.Read(ref timeoutsInARow) != 0) Interlocked.Exchange(ref timeoutsInARow, 0);
        }

        private void Failed(Attempt attempt, Exception ex)
        {
            var isTimeout = ex is TimeoutException;
            if (attempt == Attempt.Normal && isTimeout && Interlocked.Increment(ref timeoutsInARow) < TimeoutsToBreak) return;

            var previous = Interlocked.Exchange(ref skipUntil, Deadline(timeProvider.GetTimestamp()));
            if (previous == 0) logger.LogWarning("The token cache is unreachable; introspecting every token until it's back: {Error}", ex.Message);
        }

        private long Deadline(long now) => Math.Max(1, now + (long)(BreakDuration.TotalSeconds * timeProvider.TimestampFrequency));
    }
}
