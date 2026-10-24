using MongoDB.Bson;
using MongoDB.Driver;

namespace Streetwriters.Identity.Tests;

/// <summary>Instances of Identity start together on deploys, and must all come up, on a new database or an existing one.</summary>
public sealed class ConcurrentStartTests : IAsyncLifetime
{
    private readonly string database = "concurrent_" + Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await new OpenIddictHost { DatabaseName = database }.Database.Client.DropDatabaseAsync(database);

    private async Task StartTogetherAsync(int instances)
    {
        var hosts = Enumerable.Range(0, instances).Select((_) => new OpenIddictHost { DatabaseName = database, KeepDatabase = true }).ToList();
        try
        {
            await Task.WhenAll(hosts.Select((host) => host.InitializeAsync()));
        }
        finally
        {
            foreach (var host in hosts.Where((h) => h.Host != null)) await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Instances_start_together()
    {
        // a new database, where each creates what's missing, then an existing one, where each finds it
        for (var round = 0; round < 5; round++) await StartTogetherAsync(4);

        var probe = new OpenIddictHost { DatabaseName = database };
        Assert.Equal(1, await probe.Database.GetCollection<BsonDocument>("openiddict.applications").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(2, await probe.Database.GetCollection<BsonDocument>("openiddict.keys").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        // a deploy that changes the client: every instance tries to update it
        var applications = probe.Database.GetCollection<BsonDocument>("openiddict.applications");
        var update = Builders<BsonDocument>.Update.Pull("permissions", "gt:legacy_refresh_token");
        Assert.Equal(1, (await applications.UpdateOneAsync(FilterDefinition<BsonDocument>.Empty, update)).ModifiedCount);
        await StartTogetherAsync(4);
        var permissions = (await applications.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync())["permissions"].AsBsonArray.Select((p) => p.AsString);
        Assert.Contains("gt:legacy_refresh_token", permissions);
    }
}
