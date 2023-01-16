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
using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using IdentityModel.AspNetCore.OAuth2Introspection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using Notesnook.API.Accessors;
using Notesnook.API.Authorization;
using Notesnook.API.Extensions;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Data;
using Streetwriters.Data.DbContexts;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API
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
            var dbSettings = new DbSettings
            {
                ConnectionString = Constants.MONGODB_CONNECTION_STRING,
                DatabaseName = Constants.MONGODB_DATABASE_NAME
            };
            services.AddSingleton<IDbSettings>(dbSettings);

            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddDefaultCors();

            services.AddDistributedMemoryCache(delegate (MemoryDistributedCacheOptions cacheOptions)
            {
                cacheOptions.SizeLimit = 262144000L;
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Notesnook", policy =>
                {
                    policy.AuthenticationSchemes.Add("introspection");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new NotesnookUserRequirement());
                });
                options.AddPolicy("Sync", policy =>
                {
                    policy.AuthenticationSchemes.Add("introspection");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new SyncRequirement());
                });
                options.AddPolicy("Verified", policy =>
                {
                    policy.AuthenticationSchemes.Add("introspection");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new EmailVerifiedRequirement());
                });
                options.AddPolicy("Pro", policy =>
                {
                    policy.AuthenticationSchemes.Add("introspection");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new ProUserRequirement());
                });
                options.AddPolicy("BasicAdmin", policy =>
                {
                    policy.AuthenticationSchemes.Add("BasicAuthentication");
                    policy.RequireClaim(ClaimTypes.Role, "Admin");
                });

                options.DefaultPolicy = options.GetPolicy("Notesnook");
            }).AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultTransformer>(); ;

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddOAuth2Introspection("introspection", options =>
            {
                options.Authority = Servers.IdentityServer.ToString();
                options.ClientSecret = Constants.NOTESNOOK_API_SECRET;
                options.ClientId = "notesnook";
                options.DiscoveryPolicy.RequireHttps = false;
                options.TokenRetriever = new Func<HttpRequest, string>(req =>
                {
                    var fromHeader = TokenRetrieval.FromAuthorizationHeader();
                    var fromQuery = TokenRetrieval.FromQueryString();   //needed for signalr and ws/wss conections to be authed via jwt
                    return fromHeader(req) ?? fromQuery(req);
                });

                options.Events.OnTokenValidated = (context) =>
                {
                    if (long.TryParse(context.Principal.FindFirst("exp")?.Value, out long expiryTime))
                    {
                        context.Properties.ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(expiryTime);
                    }
                    context.Properties.AllowRefresh = true;
                    context.Properties.IsPersistent = true;
                    context.HttpContext.User = context.Principal;
                    return Task.CompletedTask;
                };
                options.SaveToken = true;
                options.EnableCaching = true;
                options.CacheDuration = TimeSpan.FromMinutes(30);
            });

            if (!BsonClassMap.IsClassMapRegistered(typeof(UserSettings)))
            {
                BsonClassMap.RegisterClassMap<UserSettings>();
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(EncryptedData)))
            {
                BsonClassMap.RegisterClassMap<EncryptedData>();
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(CallToAction)))
            {
                BsonClassMap.RegisterClassMap<CallToAction>();
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(Announcement)))
            {
                BsonClassMap.RegisterClassMap<Announcement>();
            }

            services.AddScoped<IDbContext, MongoDbContext>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped(typeof(Repository<>));
            services.AddScoped(typeof(SyncItemsRepository<>));

            services.TryAddTransient<ISyncItemsRepositoryAccessor, SyncItemsRepositoryAccessor>();
            services.TryAddTransient<IUserService, UserService>();
            services.TryAddTransient<IS3Service, S3Service>();

            services.AddControllers();

            services.AddHealthChecks().AddMongoDb(dbSettings.ConnectionString, dbSettings.DatabaseName, "database-check");
            services.AddSignalR((hub) =>
            {
                hub.MaximumReceiveMessageSize = 100 * 1024 * 1024;
                hub.EnableDetailedErrors = true;
            }).AddMessagePackProtocol();

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });
            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });
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

            app.UseResponseCompression();

            app.UseCors("notesnook");
            app.UseVersion();

            app.UseWamp(WampServers.NotesnookServer, (realm, server) =>
            {
                IUserService service = app.GetScopedService<IUserService>();
                realm.Subscribe<DeleteUserMessage>(server.Topics.DeleteUserTopic, async (ev) =>
                {
                    await service.DeleteUserAsync(ev.UserId, null);
                });
            });

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");
                endpoints.MapHub<SyncHub>("/hubs/sync", options =>
                {
                    options.CloseOnAuthenticationExpiration = false;
                    options.Transports = HttpTransportType.WebSockets;
                });
            });
        }
    }
}
