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
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Interfaces;
using WampSharp.V2;
using WampSharp.V2.Client;

namespace Streetwriters.Common
{
    public class WampServer
    {
        public required string Endpoint { get; set; }
        public required string Address { get; set; }
        public required string Realm { get; set; }

        private IWampChannel? channel = null;
        private async Task<IWampChannel> GetChannelAsync()
        {
            if (channel != null && channel.RealmProxy.Monitor.IsConnected)
                return channel;
            channel = await WampHelper.OpenWampChannelAsync(Address, Realm);
            return channel;
        }

        public async Task<V> GetServiceAsync<V>(Func<Task>? onDisconnect = null) where V : class
        {
            var channel = await GetChannelAsync();
            if (onDisconnect != null)
            {
                channel.RealmProxy.Monitor.ConnectionBroken += (s, e) => onDisconnect();
                channel.RealmProxy.Monitor.ConnectionError += (s, e) => onDisconnect();
            }
            return channel.RealmProxy.Services.GetCalleeProxy<V>();
        }

        public async Task PublishMessageAsync<V>(string topic, V message)
        {
            IWampChannel channel = await GetChannelAsync();
            WampHelper.PublishMessage(channel.RealmProxy, topic, message);
        }

        public async Task PublishMessagesAsync<V>(string topic, IEnumerable<V> messages)
        {
            IWampChannel channel = await GetChannelAsync();
            WampHelper.PublishMessages(channel.RealmProxy, topic, messages);
        }
    }

    public class WampServers
    {
        public static WampServer MessengerServer { get; } = new WampServer
        {
            Endpoint = "/wamp",
            Address = $"{Servers.MessengerServer.WS()}/wamp",
            Realm = "messages",
        };

        public static WampServer SubscriptionServer { get; } = new WampServer
        {
            Endpoint = "/wamp",
            Address = $"{Servers.SubscriptionServer.WS()}/wamp",
            Realm = "messages",
        };

        public static WampServer IdentityServer { get; } = new WampServer
        {
            Endpoint = "/wamp",
            Address = $"{Servers.IdentityServer.WS()}/wamp",
            Realm = "messages",
        };

        public static WampServer NotesnookServer { get; } = new WampServer
        {
            Endpoint = "/wamp",
            Address = $"{Servers.NotesnookAPI.WS()}/wamp",
            Realm = "messages",
        };
    }

    public struct MessengerServerTopics
    {
        public const string SendSSETopic = "co.streetwriters.sse.send";
    }

    public struct SubscriptionServerTopics
    {
        public const string UserSubscriptionServiceTopic = "co.streetwriters.subscriptions.subscriptions";
        public const string CreateSubscriptionTopic = "co.streetwriters.subscriptions.create";
        public const string CreateSubscriptionV2Topic = "co.streetwriters.subscriptions.v2.create";
        public const string DeleteSubscriptionTopic = "co.streetwriters.subscriptions.delete";
    }

    public struct IdentityServerTopics
    {
        public const string UserAccountServiceTopic = "co.streetwriters.identity.users";
        public const string ClearCacheTopic = "co.streetwriters.identity.clear_cache";
        public const string DeleteUserTopic = "co.streetwriters.identity.delete_user";
    }
}