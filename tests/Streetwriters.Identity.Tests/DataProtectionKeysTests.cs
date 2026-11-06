using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Streetwriters.Identity.Tests;

public sealed class DataProtectionKeysTests : IAsyncLifetime
{
    private readonly string database = "identity_dataprotection_" + Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await Host().Database.Client.DropDatabaseAsync(database);

    private OpenIddictHost Host() => new() { DatabaseName = database, KeepDatabase = true };

    // the purpose UserManager's password reset and email confirmation tokens use
    private static IDataProtector Protector(IdentityHost host) =>
        host.Host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("DataProtectorTokenProvider");

    [Fact]
    public async Task Tokens_from_one_instance_validate_on_another_and_after_a_restart()
    {
        var first = Host();
        var second = Host();
        await first.InitializeAsync();
        await second.InitializeAsync();
        string token;
        try
        {
            token = Protector(first).Protect("reset");
            Assert.Equal("reset", Protector(second).Unprotect(token));
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }

        var restarted = Host();
        await restarted.InitializeAsync();
        try { Assert.Equal("reset", Protector(restarted).Unprotect(token)); }
        finally { await restarted.DisposeAsync(); }
    }

    [Fact]
    public async Task Keys_are_stored_encrypted()
    {
        var host = Host();
        await host.InitializeAsync();
        try
        {
            Protector(host).Protect("reset");
            var keys = await host.Database.GetCollection<BsonDocument>("dataprotection.keys").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.NotEmpty(keys);
            Assert.All(keys, (key) =>
            {
                Assert.Contains("<encryptedKey", key["xml"].AsString);
                Assert.DoesNotContain("<masterKey", key["xml"].AsString);
            });
        }
        finally { await host.DisposeAsync(); }
    }
}
