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
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notesnook.API.Hubs;

namespace Notesnook.API.Services
{
    /// <summary>
    /// Tells a user's other devices that one of them pushed, after the push call returns: the send goes through the Redis
    /// backplane, which can be down or slow, and devices that miss it sync on their own anyway.
    /// </summary>
    public sealed class PushNotifier(IHubContext<SyncV2Hub, ISyncV2HubClient> hub, ILogger<PushNotifier> logger) : BackgroundService
    {
        public const int Senders = 4;

        // one pending entry per connection, so a busy device can't crowd out others while sends are slow
        private readonly ConcurrentDictionary<Pusher, string?> pending = new();
        private readonly Channel<Pusher> queue = Channel.CreateUnbounded<Pusher>();
        private int failing;

        private readonly record struct Pusher(string UserId, string ConnectionId);

        /// <param name="deviceId">null to send only PushCompleted.</param>
        public void Notify(string userId, string connectionId, string? deviceId)
        {
            var pusher = new Pusher(userId, connectionId);
            if (pending.TryAdd(pusher, deviceId)) queue.Writer.TryWrite(pusher);
            else if (deviceId != null) pending.TryUpdate(pusher, deviceId, null);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // a background service that throws stops the host
            try
            {
                await Task.WhenAll(Enumerable.Range(0, Senders).Select((_) => SendAsync(stoppingToken)));
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task SendAsync(CancellationToken stoppingToken)
        {
            await foreach (var pusher in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // removed before sending, so a push during the send is announced again
                if (!pending.TryRemove(pusher, out var deviceId)) continue;
                try
                {
                    var others = hub.Clients.GroupExcept(pusher.UserId, pusher.ConnectionId);
                    await (deviceId == null ? others.PushCompleted() : Task.WhenAll(others.PushCompleted(), others.PushCompletedV2(deviceId)));
                    if (Interlocked.Exchange(ref failing, 0) == 1)
                        logger.LogInformation("Notifying users' other devices of their pushes works again");
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    if (Interlocked.Exchange(ref failing, 1) == 0)
                        logger.LogWarning(ex, "Failed to notify users' other devices of their pushes; they'll sync on their own");
                }
            }
        }
    }
}
