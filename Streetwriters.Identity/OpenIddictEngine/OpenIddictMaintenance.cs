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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using OpenIddict.MongoDb;
using OpenIddict.MongoDb.Models;
using Streetwriters.Identity.Legacy;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>The indexes OpenIddict's MongoDB stores need, which they don't create themselves.</summary>
    public class StoreIndexes(IOptionsMonitor<OpenIddictMongoDbOptions> options) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var database = options.CurrentValue.Database!;

            var applications = database.GetCollection<OpenIddictMongoDbApplication>(options.CurrentValue.ApplicationsCollectionName);
            await applications.Indexes.CreateOneAsync(new CreateIndexModel<OpenIddictMongoDbApplication>(
                Builders<OpenIddictMongoDbApplication>.IndexKeys.Ascending((a) => a.ClientId), new CreateIndexOptions { Unique = true }), cancellationToken: cancellationToken);

            var authorizations = database.GetCollection<OpenIddictMongoDbAuthorization>(options.CurrentValue.AuthorizationsCollectionName);
            var authorizationKeys = Builders<OpenIddictMongoDbAuthorization>.IndexKeys;
            await authorizations.Indexes.CreateManyAsync([
                new(authorizationKeys.Ascending((a) => a.ApplicationId).Ascending((a) => a.Scopes).Ascending((a) => a.Status).Ascending((a) => a.Subject).Ascending((a) => a.Type)),
                new(authorizationKeys.Ascending((a) => a.Subject)),
            ], cancellationToken);

            var scopes = database.GetCollection<OpenIddictMongoDbScope>(options.CurrentValue.ScopesCollectionName);
            await scopes.Indexes.CreateOneAsync(new CreateIndexModel<OpenIddictMongoDbScope>(
                Builders<OpenIddictMongoDbScope>.IndexKeys.Ascending((s) => s.Name), new CreateIndexOptions { Unique = true }), cancellationToken: cancellationToken);

            var tokens = database.GetCollection<OpenIddictMongoDbToken>(options.CurrentValue.TokensCollectionName);
            var tokenKeys = Builders<OpenIddictMongoDbToken>.IndexKeys;
            await tokens.Indexes.CreateManyAsync([
                // every introspection and refresh looks tokens up by it
                new(tokenKeys.Ascending((t) => t.ReferenceId), new CreateIndexOptions<OpenIddictMongoDbToken>
                {
                    PartialFilterExpression = Builders<OpenIddictMongoDbToken>.Filter.Exists((t) => t.ReferenceId),
                    Unique = true
                }),
                new(tokenKeys.Ascending((t) => t.ApplicationId).Ascending((t) => t.Status).Ascending((t) => t.Subject).Ascending((t) => t.Type)),
                new(tokenKeys.Ascending((t) => t.AuthorizationId)),
                new(tokenKeys.Ascending((t) => t.Subject)),
            ], cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Removes tokens and sessions that can no longer be used, as TokenCleanupJob did for IdentityServer4's grants.</summary>
    public class TokenPruning(IServiceProvider services, TimeProvider timeProvider, ILogger<TokenPruning> logger) : BackgroundService
    {
        // past the longest lifetime (a refresh token's 45 days), so nothing that could still be used goes
        private static readonly TimeSpan Age = TimeSpan.FromDays(46);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(12), timeProvider);
            do
            {
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var threshold = timeProvider.GetUtcNow() - Age;
                    var tokens = await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold, stoppingToken);
                    var authorizations = await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold, stoppingToken);
                    // IdentityServer4's grants, and the copies of OpenIddict's, which TokenCleanupJob removed until the switch
                    var grants = await scope.ServiceProvider.GetRequiredService<LegacyGrants>().RemoveExpiredAsync();
                    logger.LogInformation("Pruned {Tokens} tokens, {Authorizations} authorizations and {Grants} IdentityServer4 grants", tokens, authorizations, grants);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Failed to prune tokens");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
