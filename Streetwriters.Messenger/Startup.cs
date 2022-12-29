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
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Streetwriters.Common;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Messages;
using Streetwriters.Messenger.Helpers;
using Streetwriters.Messenger.Services;
using WampSharp.AspNetCore.WebSockets.Server;
using WampSharp.Binding;
using WampSharp.V2;
using WampSharp.V2.Realm;

namespace Streetwriters.Messenger
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddControllers();

            JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddCors();
            services.AddDistributedMemoryCache(delegate (MemoryDistributedCacheOptions cacheOptions)
            {
                cacheOptions.SizeLimit = 262144000L;
            });

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddOAuth2Introspection("introspection", options =>
            {
                options.Authority = Servers.IdentityServer.ToString();
                options.ClientSecret = Constants.NOTESNOOK_API_SECRET;
                options.ClientId = "notesnook";
                options.SaveToken = true;
                options.EnableCaching = true;
                options.CacheDuration = TimeSpan.FromMinutes(30);
                // TODO
                options.DiscoveryPolicy.RequireHttps = false;
            });

            services.AddServerSentEvents();
            services.AddSingleton<IHostedService, HeartbeatService>();
            services.AddResponseCompression(options =>
            {
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "text/event-stream" });
            });
            services.AddHealthChecks();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (!env.IsDevelopment())
            {
                app.UseForwardedHeaders(new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                });
            }

            app.UseCors("notesnook");

            app.UseRouting();

            app.UseAuthorization();
            app.UseAuthentication();

            var options = new ServerSentEventsOptions();
            options.Authorization = new ServerSentEventsAuthorization()
            {
                AuthenticationSchemes = "introspection"
            };
            app.MapServerSentEvents("/sse", options);

            app.UseWamp(WampServers.MessengerServer, (realm, server) =>
            {
                IServerSentEventsService service = app.ApplicationServices.GetRequiredService<IServerSentEventsService>();
                realm.Subscribe<SendSSEMessage>(server.Topics.SendSSETopic, async (ev) =>
                {
                    var message = JsonSerializer.Serialize(ev.Message);
                    if (ev.SendToAll)
                    {
                        await SSEHelper.SendEventToAllUsersAsync(message, service);
                    }
                    else
                    {
                        await SSEHelper.SendEventToUserAsync(message, service, ev.UserId, ev.OriginTokenId);
                    }
                });
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");
            });
        }
    }
}
