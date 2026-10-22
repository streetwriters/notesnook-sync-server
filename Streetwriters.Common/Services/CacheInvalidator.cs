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
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Streetwriters.Common.Services
{
    // Identity's ClearCache reaches only one replica of a service, so it is fanned out to the others over Redis.
    // The cache itself stays in memory so that Redis being down doesn't take authentication down with it.
    public class CacheInvalidator(Server server, IDistributedCache cache, ILogger<CacheInvalidator> logger) : IHostedService
    {
        private readonly RedisChannel channel = RedisChannel.Literal($"clear_cache:{server.Id}");
        private ConnectionMultiplexer? redis;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Constants.CACHE_REDIS_CONNECTION_STRING)) return;

            var options = ConfigurationOptions.Parse(Constants.CACHE_REDIS_CONNECTION_STRING);
            options.AbortOnConnectFail = false;
            redis = await ConnectionMultiplexer.ConnectAsync(options);
            await redis.GetSubscriber().SubscribeAsync(channel, (_, value) =>
            {
                var keys = JsonSerializer.Deserialize<string[]>(value.ToString());
                if (keys != null) RemoveLocally(keys);
            });
        }

        public async Task ClearAsync(IEnumerable<string> keys)
        {
            if (redis == null)
            {
                RemoveLocally(keys);
                return;
            }

            try
            {
                // every replica, this one included, removes the keys when the message comes back
                await redis.GetSubscriber().PublishAsync(channel, JsonSerializer.Serialize(keys));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish cache invalidation, clearing only this instance");
                RemoveLocally(keys);
            }
        }

        private void RemoveLocally(IEnumerable<string> keys)
        {
            foreach (var key in keys) cache.Remove(key);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (redis != null) await redis.DisposeAsync();
        }
    }
}
