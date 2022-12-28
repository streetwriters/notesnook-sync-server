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
using System.Collections.Generic;
using System.Linq;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Streetwriters.Common
{
    public class Clients
    {
        private static IClient Notesnook = new Client
        {
            Id = "notesnook",
            Name = "Notesnook",
            ProductIds = new string[]
            {
                "com.streetwriters.notesnook",
                "org.streetwriters.notesnook",
                "com.streetwriters.notesnook.sub.mo",
                "com.streetwriters.notesnook.sub.yr",
                "com.streetwriters.notesnook.sub.mo.15",
                "com.streetwriters.notesnook.sub.yr.15",
                "com.streetwriters.notesnook.sub.yr.trialoffer",
                "com.streetwriters.notesnook.sub.mo.trialoffer",
                "com.streetwriters.notesnook.sub.mo.tier1",
                "com.streetwriters.notesnook.sub.yr.tier1",
                "com.streetwriters.notesnook.sub.mo.tier2",
                "com.streetwriters.notesnook.sub.yr.tier2",
                "com.streetwriters.notesnook.sub.mo.tier3",
                "com.streetwriters.notesnook.sub.yr.tier3",
                "9822", // dev
                "648884", // monthly tier 1
                "658759", // yearly tier 1
                "763942", // monthly tier 2
                "763945", // yearly tier 2
                "763943", // monthly tier 3
                "763944", // yearly tier 3
            },
            SenderEmail = "support@notesnook.com",
            SenderName = "Notesnook",
            Type = ApplicationType.NOTESNOOK,
            AppId = ApplicationType.NOTESNOOK,
            WelcomeEmailTemplateId = "d-87768b3ee17d41fdbe4bcf0eb2583682"
        };

        public static Dictionary<string, IClient> ClientsMap = new Dictionary<string, IClient>
        {
            { "notesnook", Notesnook }
        };

        public static IClient FindClientById(string id)
        {
            if (!IsValidClient(id)) return null;
            return ClientsMap[id];
        }

        public static IClient FindClientByAppId(ApplicationType appId)
        {
            switch (appId)
            {
                case ApplicationType.NOTESNOOK:
                    return ClientsMap["notesnook"];
            }
            return null;
        }

        public static IClient FindClientByProductId(string productId)
        {
            foreach (var client in ClientsMap)
            {
                if (client.Value.ProductIds.Contains(productId)) return client.Value;
            }
            return null;
        }

        public static bool IsValidClient(string id)
        {
            return ClientsMap.ContainsKey(id);
        }

        public static SubscriptionProvider? PlatformToSubscriptionProvider(string platform)
        {
            return platform switch
            {
                "ios" => SubscriptionProvider.APPLE,
                "android" => SubscriptionProvider.GOOGLE,
                "web" => SubscriptionProvider.PADDLE,
                _ => null,
            };
        }
    }
}