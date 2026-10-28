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
using System.Linq;
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
using static OpenIddict.Abstractions.OpenIddictConstants;
using Streetwriters.Identity.Legacy;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>The indexes OpenIddict's MongoDB stores need, which they don't create themselves, and the token records'.</summary>
    public class StoreIndexes(IOptionsMonitor<OpenIddictMongoDbOptions> options, TokenRecords records) : IHostedService
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

            await records.CreateIndexesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Removes tokens and sessions that can no longer be used, as TokenCleanupJob and IdentityServer4 did with its grants.
    /// </summary>
    /// <remarks>OpenIddict's own pruning goes by age, which would keep every redeemed refresh token for 45 days.</remarks>
    public class TokenPruner(IOptionsMonitor<OpenIddictMongoDbOptions> options, IOpenIddictAuthorizationManager authorizations, TokenRecords records, LegacyGrants legacyGrants, TimeProvider timeProvider)
    {
        private const int BatchSize = 10_000;

        // sessions are kept a day after their last token goes
        private static readonly TimeSpan SessionGrace = TimeSpan.FromDays(1);

        public async Task<(long Tokens, long Authorizations, long Grants)> PruneAsync(CancellationToken cancellationToken = default)
        {
            var tokens = options.CurrentValue.Database!.GetCollection<OpenIddictMongoDbToken>(options.CurrentValue.TokensCollectionName);
            var filter = Builders<OpenIddictMongoDbToken>.Filter;
            // redeemed tokens stay until they expire: they're replaced refresh tokens that still work
            var unusable = filter.Or(
                filter.Lt((t) => t.ExpirationDate, timeProvider.GetUtcNow().UtcDateTime),
                filter.In((t) => t.Status, [Statuses.Revoked, Statuses.Rejected, Statuses.Inactive]));

            long prunedTokens = 0;
            while (true)
            {
                var ids = await tokens.Find(unusable).Project((t) => t.Id).Limit(BatchSize).ToListAsync(cancellationToken);
                if (ids.Count == 0) break;
                await records.RemoveAsync(ids.Select((id) => id.ToString()));
                prunedTokens += (await tokens.DeleteManyAsync(filter.In((t) => t.Id, ids), cancellationToken)).DeletedCount;
            }

            var prunedAuthorizations = await authorizations.PruneAsync(timeProvider.GetUtcNow() - SessionGrace, cancellationToken);
            // IdentityServer4's grants, and the copies of OpenIddict's, which TokenCleanupJob removed until the switch
            var prunedGrants = await legacyGrants.RemoveExpiredAsync();
            return (prunedTokens, prunedAuthorizations, prunedGrants);
        }
    }

    public class TokenPruning(IServiceProvider services, TimeProvider timeProvider, ILogger<TokenPruning> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(12), timeProvider);
            do
            {
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var (tokens, authorizations, grants) = await scope.ServiceProvider.GetRequiredService<TokenPruner>().PruneAsync(stoppingToken);
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
