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
using WampSharp.V2.Client;

namespace Streetwriters.Common
{
    public class WampServer<T> where T : new()
    {
        private readonly ConcurrentDictionary<string, IWampRealmProxy> Channels = new();

        public string Endpoint { get; set; }
        public string Address { get; set; }
        public T Topics { get; set; } = new T();
        public string Realm { get; set; }

        private async Task<IWampRealmProxy> GetChannelAsync(string topic)
        {
            if (!Channels.TryGetValue(topic, out IWampRealmProxy channel) || !channel.Monitor.IsConnected)
            {
                channel = await WampHelper.OpenWampChannelAsync(Address, Realm);
                Channels.AddOrUpdate(topic, (key) => channel, (key, old) => channel);
            }
            return channel;
        }

        public async Task<V> GetServiceAsync<V>(string topic) where V : class
        {
            var channel = await GetChannelAsync(topic);
            return channel.Services.GetCalleeProxy<V>();
        }

        public async Task PublishMessageAsync<V>(string topic, V message)
        {
            IWampRealmProxy channel = await GetChannelAsync(topic);
            WampHelper.PublishMessage(channel, topic, message);
        }

        public async Task PublishMessagesAsync<V>(string topic, IEnumerable<V> messages)
        {
            IWampRealmProxy channel = await GetChannelAsync(topic);
            WampHelper.PublishMessages(channel, topic, messages);
        }
    }

    public class WampServers
    {
        public static WampServer<MessengerServerTopics> MessengerServer { get; } = new WampServer<MessengerServerTopics>
        {
            Endpoint = "/wamp",
            Address = $"{Servers.MessengerServer.WS()}/wamp",
            Realm = "messages",
        };

        public static WampServer<SubscriptionServerTopics> SubscriptionServer { get; } = new WampServer<SubscriptionServerTopics>
        {
            Endpoint = "/wamp",
            Address = $"{Servers.SubscriptionServer.WS()}/wamp",
            Realm = "messages",
        };

        public static WampServer<IdentityServerTopics> IdentityServer { get; } = new WampServer<IdentityServerTopics>
        {
            Endpoint = "/wamp",
            Address = $"{Servers.IdentityServer.WS()}/wamp",
            Realm = "messages",
        };

        public static WampServer<NotesnookServerTopics> NotesnookServer { get; } = new WampServer<NotesnookServerTopics>
        {
            Endpoint = "/wamp",
            Address = $"{Servers.NotesnookAPI.WS()}/wamp",
            Realm = "messages",
        };
    }

    public class MessengerServerTopics
    {
        public const string SendSSETopic = "co.streetwriters.sse.send";
    }

    public class SubscriptionServerTopics
    {
        public const string UserSubscriptionServiceTopic = "co.streetwriters.subscriptions.subscriptions";

        public const string CreateSubscriptionTopic = "co.streetwriters.subscriptions.create";
        public const string CreateSubscriptionV2Topic = "co.streetwriters.subscriptions.v2.create";
        public const string DeleteSubscriptionTopic = "co.streetwriters.subscriptions.delete";
    }

    public class IdentityServerTopics
    {
        public const string UserAccountServiceTopic = "co.streetwriters.identity.users";
        public const string ClearCacheTopic = "co.streetwriters.identity.clear_cache";
        public const string DeleteUserTopic = "co.streetwriters.identity.delete_user";
    }

    public class NotesnookServerTopics
    {
    }
}