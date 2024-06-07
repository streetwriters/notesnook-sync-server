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
using System.Linq;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;

namespace Streetwriters.Common
{
    public class Clients
    {
        public static readonly Client Notesnook = new()
        {
            Id = "notesnook",
            Name = "Notesnook",
            SenderEmail = Constants.NOTESNOOK_SENDER_EMAIL,
            SenderName = Constants.NOTESNOOK_SENDER_NAME,
            Type = ApplicationType.NOTESNOOK,
            AppId = ApplicationType.NOTESNOOK,
            AccountRecoveryRedirectURL = $"{Constants.NOTESNOOK_APP_HOST}/account/recovery",
            EmailConfirmedRedirectURL = $"{Constants.NOTESNOOK_APP_HOST}/account/verified",
            OnEmailConfirmed = async (userId) =>
            {
                await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
                {
                    UserId = userId,
                    Message = new Message
                    {
                        Type = "emailConfirmed",
                        Data = null
                    }
                });
            }
        };

        public static Dictionary<string, Client> ClientsMap = new()
        {
            { "notesnook", Notesnook }
        };

        public static Client FindClientById(string id)
        {
            if (!IsValidClient(id)) return null;
            return ClientsMap[id];
        }

        public static Client FindClientByAppId(ApplicationType appId)
        {
            switch (appId)
            {
                case ApplicationType.NOTESNOOK:
                    return ClientsMap["notesnook"];
            }
            return null;
        }

        public static bool IsValidClient(string id)
        {
            return ClientsMap.ContainsKey(id);
        }
    }
}