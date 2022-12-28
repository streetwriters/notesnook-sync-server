using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Streetwriters.Common.Helpers;
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

        public async Task PublishMessageAsync<V>(string topic, V message)
        {
            try
            {
                IWampRealmProxy channel;
                if (Channels.ContainsKey(topic))
                    channel = Channels[topic];
                else
                {
                    channel = await WampHelper.OpenWampChannelAsync<V>(this.Address, this.Realm);
                    Channels.TryAdd(topic, channel);
                }
                if (!channel.Monitor.IsConnected)
                {
                    Channels.TryRemove(topic, out IWampRealmProxy value);
                    await PublishMessageAsync<V>(topic, message);
                    return;
                }
                WampHelper.PublishMessage<V>(channel, topic, message);
            }
            catch (Exception ex)
            {
                await Slogger<WampServer<T>>.Error(nameof(PublishMessageAsync), ex.ToString());
                throw ex;
            }
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
        public string SendSSETopic => "com.streetwriters.sse.send";
    }

    public class SubscriptionServerTopics
    {
        public string CreateSubscriptionTopic => "com.streetwriters.subscriptions.create";
        public string DeleteSubscriptionTopic => "com.streetwriters.subscriptions.delete";
    }

    public class IdentityServerTopics
    {
        public string CreateSubscriptionTopic => "com.streetwriters.subscriptions.create";
        public string DeleteSubscriptionTopic => "com.streetwriters.subscriptions.delete";
    }

    public class NotesnookServerTopics
    {
        public string DeleteUserTopic => "com.streetwriters.notesnook.user.delete";
    }
}