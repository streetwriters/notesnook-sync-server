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
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WampSharp.AspNetCore.WebSockets.Server;
using WampSharp.Binding;
using WampSharp.V2;
using WampSharp.V2.Realm;

namespace Streetwriters.Common.Extensions
{
    public static class AppBuilderExtensions
    {
        public static IApplicationBuilder UseVersion(this IApplicationBuilder app, Server server)
        {
            app.Map("/version", (app) =>
            {
                app.Run(async context =>
                {
                    context.Response.ContentType = "application/json";
                    var data = new Dictionary<string, object>
                    {
                        { "version", Constants.COMPATIBILITY_VERSION },
                        { "id", server.Id ?? "unknown" },
                        { "instance", Constants.INSTANCE_NAME }
                    };
                    await context.Response.WriteAsync(JsonSerializer.Serialize(data));
                });
            });
            return app;
        }

        public static IApplicationBuilder UseWamp(this IApplicationBuilder app, WampServer server, Action<IWampHostedRealm, WampServer> action)
        {
            WampHost host = new();

            // WAMP is only served on its internal port, and that port serves nothing else.
            var expectedToken = Encoding.UTF8.GetBytes(WampServer.AuthToken);
            app.MapWhen(context => context.Connection.LocalPort == server.Port, wampApp =>
            {
                wampApp.Map(server.Endpoint, builder =>
                {
                    builder.Use(async (context, next) =>
                    {
                        var token = Encoding.UTF8.GetBytes(context.Request.Query[WampServer.AuthTokenQueryKey].ToString());
                        if (!CryptographicOperations.FixedTimeEquals(token, expectedToken))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return;
                        }
                        await next();
                    });
                    builder.UseWebSockets();
                    host.RegisterTransport(new AspNetCoreWebSocketTransport(builder),
                                           new JTokenJsonBinding(),
                                           new JTokenMsgpackBinding());
                });
                wampApp.Run(context =>
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                });
            });

            host.Open();

            action.Invoke(host.RealmContainer.GetRealmByName(server.Realm), server);

            return app;
        }

        public static T GetService<T>(this IApplicationBuilder app) where T : notnull
        {
            return app.ApplicationServices.GetRequiredService<T>();
        }

        public static T GetScopedService<T>(this IApplicationBuilder app) where T : notnull
        {
            using var scope = app.ApplicationServices.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        public static IApplicationBuilder UseForwardedHeadersWithKnownProxies(this IApplicationBuilder app, IWebHostEnvironment env, string forwardedForHeaderName = null)
        {
            if (!env.IsDevelopment())
            {
                var forwardedHeadersOptions = new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                };

                if (!string.IsNullOrEmpty(forwardedForHeaderName))
                {
                    forwardedHeadersOptions.ForwardedForHeaderName = forwardedForHeaderName;
                }

                foreach (var proxy in Constants.KNOWN_PROXIES)
                {
                    forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                app.UseForwardedHeaders(forwardedHeadersOptions);
            }

            return app;
        }
    }
}