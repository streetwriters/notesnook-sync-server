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
using System.IO;
using AspNetCore.Identity.Mongo;
using IdentityServer4.ResponseHandling;
using IdentityServer4.Services;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using Streetwriters.Common;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Helpers;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using Streetwriters.Identity.Services;
using Streetwriters.Identity.Validation;

namespace Streetwriters.Identity
{
    public class Startup
    {

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            WebHostEnvironment = environment;
        }

        private IConfiguration Configuration { get; }
        private IWebHostEnvironment WebHostEnvironment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var connectionString = Constants.MONGODB_CONNECTION_STRING;

            services.AddTransient<IEmailSender, EmailSender>();
            services.AddTransient<ISMSSender, SMSSender>();
            services.AddTransient<IPasswordHasher<User>, Argon2PasswordHasher<User>>();

            services.AddCors();

            //services.AddSingleton<IProfileService, UserService>();
            services.AddIdentityMongoDbProvider<User>(options =>
            {
                // Password settings.
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 0;

                // Lockout settings.
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // User settings.
                //options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._";
                options.User.RequireUniqueEmail = true;
            }, (options) =>
            {
                options.RolesCollection = "roles";
                options.UsersCollection = "users";
                // options.MigrationCollection = "migration";
                options.ConnectionString = connectionString;
            }).AddDefaultTokenProviders();

            var builder = services.AddIdentityServer(
            options =>
            {
                options.Events.RaiseSuccessEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseErrorEvents = true;
                options.IssuerUri = Servers.IdentityServer.ToString();
            })
            .AddExtensionGrantValidator<EmailGrantValidator>()
            .AddExtensionGrantValidator<MFAGrantValidator>()
            .AddExtensionGrantValidator<MFAPasswordGrantValidator>()
            .AddOperationalStore(options =>
            {
                options.ConnectionString = connectionString;
            })
            .AddConfigurationStore(options =>
            {
                options.ConnectionString = connectionString;
            })
            .AddAspNetIdentity<User>()
            .AddInMemoryClients(Config.Clients)
            .AddInMemoryApiResources(Config.ApiResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddKeyManagement()
            .AddFileSystemPersistence(Path.Combine(WebHostEnvironment.ContentRootPath, @"keystore"));

            services.Configure<DataProtectionTokenProviderOptions>(options =>
            {
                options.TokenLifespan = TimeSpan.FromHours(2);
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("mfa", policy =>
                {
                    policy.AddAuthenticationSchemes("Bearer+jwt");
                    policy.RequireClaim("scope", Config.MFA_GRANT_TYPE_SCOPE);
                });
            });


            services.AddLocalApiAuthentication();
            services.AddAuthentication()
            .AddJwtBearer("Bearer+jwt", options =>
            {
                options.MapInboundClaims = false;
                options.Authority = Servers.IdentityServer.ToString();
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidTypes = new[] { "at+jwt" },
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                };
            });

            services.AddTransient<IMFAService, MFAService>();
            services.AddControllers();
            services.AddTransient<IIntrospectionResponseGenerator, CustomIntrospectionResponseGenerator>();
            services.AddTransient<IProfileService, ProfileService>();
            services.AddTransient<ITokenGenerationService, TokenGenerationService>();
            services.AddTransient<ITokenResponseGenerator, TokenResponseHandler>();
            services.AddTransient<IRefreshTokenService, CustomRefreshTokenService>();
            services.AddTransient<IResourceOwnerPasswordValidator, CustomResourceOwnerValidator>();

            services.AddHealthChecks();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (!env.IsDevelopment())
            {
                app.UseForwardedHeaders(new ForwardedHeadersOptions
                {
                    ForwardedForHeaderName = "CF_CONNECTING_IP",
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                });
            }

            app.UseCors("notesnook");

            app.UseRouting();

            app.UseIdentityServer();

            app.UseAuthorization();
            app.UseAuthentication();

            app.UseWamp(WampServers.IdentityServer, (realm, server) =>
            {
                realm.Subscribe(server.Topics.CreateSubscriptionTopic, async (CreateSubscriptionMessage message) =>
                {
                    using (var serviceScope = app.ApplicationServices.CreateScope())
                    {
                        var services = serviceScope.ServiceProvider;
                        var userManager = services.GetRequiredService<UserManager<User>>();
                        await MessageHandlers.CreateSubscription.Process(message, userManager);
                    }
                });
                realm.Subscribe(server.Topics.DeleteSubscriptionTopic, async (DeleteSubscriptionMessage message) =>
                {
                    using (var serviceScope = app.ApplicationServices.CreateScope())
                    {
                        var services = serviceScope.ServiceProvider;
                        var userManager = services.GetRequiredService<UserManager<User>>();
                        await MessageHandlers.DeleteSubscription.Process(message, userManager);
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
