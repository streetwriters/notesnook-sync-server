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
using System.Threading.Tasks;
using Lib.AspNetCore.ServerSentEvents;
using System.Security.Claims;

namespace Streetwriters.Messenger.Helpers
{
    public class SSEHelper
    {
        public static async Task SendEventToUserAsync(string data, IServerSentEventsService sseService, string userId, string originTokenId = null)
        {
            var clients = sseService.GetClients().Where(c => c.User.FindFirstValue("sub") == userId);
            foreach (var client in clients)
            {
                if (originTokenId != null && client.User.FindFirstValue("jti") == originTokenId) continue;
                if (!client.IsConnected) continue;
                await client.SendEventAsync(data);
            }
        }

        public static async Task SendEventToAllUsersAsync(string data, IServerSentEventsService sseService)
        {
            await sseService.SendEventAsync(data);
        }
    }
}