using System.Reactive.Subjects;
using System.Threading.Tasks;
using Streetwriters.Common.Messages;
using WampSharp.V2;
using WampSharp.V2.Client;

namespace Streetwriters.Common.Helpers
{
    public class WampHelper
    {
        public static async Task<IWampRealmProxy> OpenWampChannelAsync<T>(string server, string realmName)
        {
            DefaultWampChannelFactory channelFactory = new DefaultWampChannelFactory();

            IWampChannel channel = channelFactory.CreateJsonChannel(server, realmName);

            await channel.Open();

            return channel.RealmProxy;
        }

        public static void PublishMessage<T>(IWampRealmProxy realm, string topicName, T message)
        {
            var subject = realm.Services.GetSubject<T>(topicName);
            subject.OnNext(message);
        }
    }
}