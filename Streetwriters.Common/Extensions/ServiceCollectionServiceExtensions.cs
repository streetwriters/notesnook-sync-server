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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Services;
using Streetwriters.Data.DbContexts;
using Streetwriters.Data.Repositories;

namespace Streetwriters.Common.Extensions
{
    public static class ServiceCollectionServiceExtensions
    {
        public static IServiceCollection AddWampServiceAccessor(this IServiceCollection services, Server server)
        {
            services.AddSingleton<WampServiceAccessor>((provider) => new WampServiceAccessor(server));
            services.AddHostedService(provider => provider.GetRequiredService<WampServiceAccessor>());
            return services;
        }

        public static IServiceCollection AddCacheInvalidator(this IServiceCollection services, Server server)
        {
            services.AddSingleton((provider) => ActivatorUtilities.CreateInstance<CacheInvalidator>(provider, server));
            services.AddHostedService(provider => provider.GetRequiredService<CacheInvalidator>());
            return services;
        }

        public static IServiceCollection AddApiExceptionHandler(this IServiceCollection services)
        {
            services.AddExceptionHandler<ApiExceptionHandler>();
            // the middleware logs every exception again, including 400s (SuppressDiagnosticsCallback is .NET 10+)
            services.AddLogging((logging) => logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None));
            return services;
        }

        public static IServiceCollection AddRepository<T>(this IServiceCollection services, string collectionName, string database) where T : class
        {
            services.AddSingleton((provider) => MongoDbContext.GetMongoCollection<T>(provider.GetRequiredService<MongoDB.Driver.IMongoClient>(), database, collectionName));
            services.AddScoped<Repository<T>>();
            return services;
        }

        public static IServiceCollection AddDefaultCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("notesnook", (b) =>
                {
                    if (Constants.NOTESNOOK_CORS_ORIGINS.Length <= 0)
                        b.AllowAnyOrigin();
                    else
                        b.WithOrigins(Constants.NOTESNOOK_CORS_ORIGINS);

                    b.AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders(["X-Object-Size", "Content-Length", "Retry-After"]);
                });
            });
            return services;
        }
    }
}
