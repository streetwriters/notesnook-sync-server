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
using Microsoft.Extensions.Hosting;
using Lib.AspNetCore.ServerSentEvents;
using Streetwriters.Messenger.Helpers;
using System.Text.Json;

namespace Streetwriters.Messenger.Services
{
    internal class HeartbeatService : BackgroundService
    {
        #region Fields
        private const string HEARTBEAT_MESSAGE_FORMAT = "Streetwriters Heartbeat ({0} UTC)";

        private readonly IServerSentEventsService _serverSentEventsService;
        #endregion

        #region Constructor
        public HeartbeatService(IServerSentEventsService serverSentEventsService)
        {
            _serverSentEventsService = serverSentEventsService;
        }
        #endregion

        #region Methods
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var message = JsonSerializer.Serialize(new
                {
                    type = "heartbeat",
                    data = JsonSerializer.Serialize(new
                    {
                        t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    })
                });
                await SSEHelper.SendEventToAllUsersAsync(message, _serverSentEventsService);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        #endregion
    }
}