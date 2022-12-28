/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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
using System.Threading.Tasks;
using Streetwriters.Common;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Messages;
using Streetwriters.Identity.Interfaces;

namespace Streetwriters.Identity.Handlers
{
    public class NotesnookHandler : IAppHandler
    {
        public string Host { get; }
        public string EmailConfirmedRedirectURL { get; }
        public string AccountRecoveryRedirectURL { get; }

        public NotesnookHandler()
        {
#if DEBUG
            Host = "http://localhost:3000";
#else
            Host = "https://app.notesnook.com";
#endif
            EmailConfirmedRedirectURL = $"{this.Host}/account/verified";
            AccountRecoveryRedirectURL = $"{this.Host}/account/recovery";
        }
        public async Task OnEmailConfirmed(string userId)
        {
            await WampServers.MessengerServer.PublishMessageAsync(WampServers.MessengerServer.Topics.SendSSETopic, new SendSSEMessage
            {
                UserId = userId,
                Message = new Message
                {
                    Type = "emailConfirmed",
                    Data = null
                }
            });
        }
    }
}