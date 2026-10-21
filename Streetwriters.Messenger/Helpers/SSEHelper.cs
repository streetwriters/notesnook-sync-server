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
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

        public static void AddToUserGroup(IServerSentEventsService sseService, IServerSentEventsClient client)
        {
            var userId = client.User?.FindFirstValue("sub");
            if (userId != null) sseService.AddToGroup(userId, client);
        }

        public static async Task SendEventToUserAsync(string data, IServerSentEventsService sseService, string userId, string? originTokenId = null, CancellationToken cancellationToken = default)
        {
            var clients = sseService.GetClients(userId)
                .Where(c => originTokenId == null || c.User?.FindFirstValue("jti") != originTokenId);

            await SendEventToClientsAsync(clients, data, cancellationToken);
        }

        public static async Task SendEventToAllUsersAsync(string data, IServerSentEventsService sseService, CancellationToken cancellationToken = default)
        {
            await SendEventToClientsAsync(sseService.GetClients(), data, cancellationToken);
        }

        private static Task SendEventToClientsAsync(IEnumerable<IServerSentEventsClient> clients, string data, CancellationToken cancellationToken)
        {
            return Task.WhenAll(clients.Where(c => c.IsConnected).Select(c => SendEventToClientAsync(c, data, cancellationToken)));
        }

        private static async Task SendEventToClientAsync(IServerSentEventsClient client, string data, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(SendTimeout);
            try
            {
                await client.SendEventAsync(data, cts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }
    }
}