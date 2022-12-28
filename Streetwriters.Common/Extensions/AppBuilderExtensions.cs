using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WampSharp.AspNetCore.WebSockets.Server;
using WampSharp.Binding;
using WampSharp.V2;
using WampSharp.V2.Realm;

namespace Streetwriters.Common.Extensions
{
    public static class AppBuilderExtensions
    {
        public static IApplicationBuilder UseWamp<T>(this IApplicationBuilder app, WampServer<T> server, Action<IWampHostedRealm, WampServer<T>> action) where T : new()
        {
            WampHost host = new WampHost();

            app.Map(server.Endpoint, builder =>
            {
                builder.UseWebSockets();
                host.RegisterTransport(new AspNetCoreWebSocketTransport(builder),
                                       new JTokenJsonBinding(),
                                       new JTokenMsgpackBinding());
            });

            host.Open();

            action.Invoke(host.RealmContainer.GetRealmByName(server.Realm), server);

            return app;
        }

        public static T GetService<T>(this IApplicationBuilder app)
        {
            return app.ApplicationServices.GetRequiredService<T>();
        }

        public static T GetScopedService<T>(this IApplicationBuilder app)
        {
            using (var scope = app.ApplicationServices.CreateScope())
            {
                return scope.ServiceProvider.GetRequiredService<T>();
            }
        }
    }
}