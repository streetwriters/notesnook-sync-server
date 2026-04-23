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

using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Lib.AspNetCore.ServerSentEvents;
using System.Security.Claims;
using System.Collections.Generic;

namespace Streetwriters.Messenger.Helpers
{
    public class SSEHelper
    {
        public static async Task SendEventToUserAsync(string data, IServerSentEventsService sseService, string userId, string? originTokenId = null, CancellationToken cancellationToken = default)
        {
            var clients = sseService.GetClients()
                .Where(c => c.User?.FindFirstValue("sub") == userId)
                .Where(c => originTokenId == null || c.User?.FindFirstValue("jti") != originTokenId);

            await SendEventToClientsAsync(clients, data, cancellationToken);
        }

        public static async Task SendEventToAllUsersAsync(string data, IServerSentEventsService sseService, CancellationToken cancellationToken = default)
        {
            await SendEventToClientsAsync(sseService.GetClients(), data, cancellationToken);
        }

        private static async Task SendEventToClientsAsync(IEnumerable<IServerSentEventsClient> clients, string data, CancellationToken cancellationToken)
        {
            foreach (var client in clients)
            {
                if (!client.IsConnected) continue;

                try
                {
                    await client.SendEventAsync(data, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                }
            }
        }
    }
}