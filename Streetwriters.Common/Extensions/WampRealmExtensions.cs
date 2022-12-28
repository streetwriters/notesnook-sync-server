using System;
using Microsoft.AspNetCore.Builder;
using Streetwriters.Common.Interfaces;
using WampSharp.AspNetCore.WebSockets.Server;
using WampSharp.Binding;
using WampSharp.V2;
using WampSharp.V2.Realm;

namespace Streetwriters.Common.Extensions
{
    public static class WampRealmExtensions
    {
        public static IDisposable Subscribe<T>(this IWampHostedRealm realm, string topicName, Action<T> onNext)
        {
            return realm.Services.GetSubject<T>(topicName).Subscribe<T>(onNext);
        }

        public static IDisposable Subscribe<T>(this IWampHostedRealm realm, string topicName, IMessageHandler<T> handler)
        {
            return realm.Services.GetSubject<T>(topicName).Subscribe<T>(async (message) => await handler.Process(message));
        }
    }
}