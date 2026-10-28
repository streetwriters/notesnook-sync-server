using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using MongoDB.Bson;
using MongoDB.Driver;
using Streetwriters.Identity.OpenIddictEngine;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// OpenIddict's signing and encryption keys are kept encrypted with a key derived from NOTESNOOK_API_SECRET,
/// so the database and its backups don't hold them, and the same keys survive restarts and secret rotations.
/// </summary>
public sealed class TokenKeyTests : IAsyncLifetime
{
    private readonly string database = "keys_" + Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET", TestEnvironment.ApiSecret);
        Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET_PREVIOUS", null);
        await Host().Database.Client.DropDatabaseAsync(database);
    }

    private OpenIddictHost Host() => new() { DatabaseName = database, KeepDatabase = true };
    private IMongoCollection<BsonDocument> Keys => Host().Database.GetCollection<BsonDocument>("openiddict.keys");

    private sealed class Client(IdentityHost host) : IdentityTestBase(host)
    {
        public HttpClient Http => client;
        public static string Email() => NewEmail();
        public new Task<Common.Models.SignupResponse> SignupAsync(string email) => base.SignupAsync(email);
        public new Task<(string AccessToken, string RefreshToken)> LoginAsync(string email) => base.LoginAsync(email);
        public new Task<bool> IsActiveAsync(string token) => base.IsActiveAsync(token);
        public new Task<HttpResponseMessage> RefreshAsync(string refreshToken, string? scope = null) => base.RefreshAsync(refreshToken, scope);
        public new static Task<(string AccessToken, string RefreshToken)> TokensAsync(HttpResponseMessage response) => IdentityTestBase.TokensAsync(response);
    }

    private static async Task OnAsync(IdentityHost host, string secret, string? previous, Func<Client, Task> steps)
    {
        // read when the keys are loaded, as the host starts
        Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET", secret);
        Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET_PREVIOUS", previous);
        try
        {
            await host.InitializeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET", TestEnvironment.ApiSecret);
            Environment.SetEnvironmentVariable("NOTESNOOK_API_SECRET_PREVIOUS", null);
        }
        try { await steps(new Client(host)); }
        finally { await host.DisposeAsync(); }
    }

    [Fact]
    public async Task Keys_are_stored_encrypted()
    {
        byte[] modulus = [];
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) =>
        {
            var configuration = JsonNode.Parse(await client.Http.GetStringAsync("/.well-known/openid-configuration"))!;
            var jwks = JsonNode.Parse(await client.Http.GetStringAsync(configuration["jwks_uri"]!.GetValue<string>()))!;
            modulus = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(jwks["keys"]![0]!["n"]!.GetValue<string>());
        });

        var keys = await Keys.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, (key) => Assert.False(key.Contains("material")));
        // a private RSA key holds its modulus as is
        var signing = keys.Single((k) => k["_id"] == "signing").ToBson();
        Assert.False(signing.AsSpan().IndexOf(modulus.AsSpan(modulus.Length / 2, 32)) >= 0);
    }

    [Fact]
    public async Task Sessions_survive_restarts()
    {
        var email = Client.Email();
        string access = "", refresh = "";
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) =>
        {
            await client.SignupAsync(email);
            (access, refresh) = await client.LoginAsync(email);
        });
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) =>
        {
            Assert.True(await client.IsActiveAsync(access));
            Assert.Equal(HttpStatusCode.OK, (await client.RefreshAsync(refresh)).StatusCode);
        });
    }

    [Fact]
    public async Task Keys_that_cant_be_decrypted_stop_the_start()
    {
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, (_) => Task.CompletedTask);
        var before = await Keys.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();

        // new keys would end every session, so it mustn't make them
        var error = await Assert.ThrowsAnyAsync<Exception>(() => OnAsync(Host(), "another-secret", null, (_) => Task.CompletedTask));
        Assert.Contains("NOTESNOOK_API_SECRET_PREVIOUS", error.ToString());
        Assert.Equal(before.ToJson(), (await Keys.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()).ToJson());
    }

    [Fact]
    public async Task Rotating_the_secret_keeps_the_keys()
    {
        var email = Client.Email();
        string access = "", refresh = "";
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) =>
        {
            await client.SignupAsync(email);
            (access, refresh) = await client.LoginAsync(email);
        });

        // started once with the old secret as the previous one, after which only the new one is needed
        await OnAsync(Host(), "rotated-secret", TestEnvironment.ApiSecret, async (client) => Assert.True(await client.IsActiveAsync(access)));
        await OnAsync(Host(), "rotated-secret", null, async (client) =>
        {
            Assert.True(await client.IsActiveAsync(access));
            Assert.Equal(HttpStatusCode.OK, (await client.RefreshAsync(refresh)).StatusCode);
        });
        await Assert.ThrowsAnyAsync<Exception>(() => OnAsync(Host(), TestEnvironment.ApiSecret, null, (_) => Task.CompletedTask));
    }

    [Fact]
    public async Task Unencrypted_keys_are_encrypted()
    {
        // as the first OpenIddict builds stored them
        using var rsa = RSA.Create(2048);
        await Keys.InsertManyAsync([
            new BsonDocument { ["_id"] = "signing", ["kid"] = "old-signing", ["material"] = new BsonBinaryData(rsa.ExportPkcs8PrivateKey()), ["created_at"] = DateTime.UtcNow },
            new BsonDocument { ["_id"] = "encryption", ["kid"] = "old-encryption", ["material"] = new BsonBinaryData(RandomNumberGenerator.GetBytes(32)), ["created_at"] = DateTime.UtcNow },
        ]);

        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) =>
        {
            var configuration = JsonNode.Parse(await client.Http.GetStringAsync("/.well-known/openid-configuration"))!;
            var jwks = JsonNode.Parse(await client.Http.GetStringAsync(configuration["jwks_uri"]!.GetValue<string>()))!;
            Assert.Equal("old-signing", jwks["keys"]![0]!["kid"]!.GetValue<string>());
        });

        var keys = await Keys.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.All(keys, (key) => Assert.False(key.Contains("material")));
        Assert.Equal(["old-encryption", "old-signing"], keys.Select((k) => k["kid"].AsString).Order());
    }

    private async Task<Dictionary<string, string>> KidsAsync() =>
        (await Keys.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()).ToDictionary((k) => k["_id"].AsString, (k) => k["kid"].AsString);

    private static async Task<List<string>> JwksAsync(Client client)
    {
        var configuration = JsonNode.Parse(await client.Http.GetStringAsync("/.well-known/openid-configuration"))!;
        var jwks = JsonNode.Parse(await client.Http.GetStringAsync(configuration["jwks_uri"]!.GetValue<string>()))!;
        return jwks["keys"]!.AsArray().Select((k) => k!["kid"]!.GetValue<string>()).ToList();
    }

    private (int Code, string Output) Command(params string[] args)
    {
        var output = new StringWriter();
        var code = TokenKeysCommand.Run(args, Host().Database, output, output, TimeProvider.System);
        return (code, output.ToString());
    }

    [Fact]
    public async Task Replacing_the_keys_ends_every_session()
    {
        var email = Client.Email();
        string access = "", refresh = "";
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) =>
        {
            await client.SignupAsync(email);
            (access, refresh) = await client.LoginAsync(email);
        });
        var old = await KidsAsync();

        Assert.Equal(0, Command("replace").Code);
        var kids = await KidsAsync();
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) =>
        {
            Assert.Equal([kids["signing:2"]], await JwksAsync(client));
            Assert.False(await client.IsActiveAsync(access));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(refresh)).StatusCode);

            // signing in again works, on the new keys
            var (newAccess, newRefresh) = await client.LoginAsync(email);
            Assert.True(await client.IsActiveAsync(newAccess));
            Assert.Equal(HttpStatusCode.OK, (await client.RefreshAsync(newRefresh)).StatusCode);
        });

        var list = Command("list").Output;
        Assert.Matches($"signing +2  {kids["signing:2"]} .* current", list);
        Assert.Matches($"signing +1  {old["signing"]} .* retired", list);
        Assert.Matches($"encryption +1  {old["encryption"]} .* retired", list);
    }

    [Fact]
    public async Task Keys_can_be_replaced_again()
    {
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, (_) => Task.CompletedTask);
        Assert.Equal(0, Command("replace").Code);
        Assert.Equal(0, Command("replace").Code);

        var kids = await KidsAsync();
        Assert.Equal(6, kids.Count);
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) => Assert.Equal([kids["signing:3"]], await JwksAsync(client)));
        Assert.Equal(4, Command("list").Output.Split('\n').Count((line) => line.Contains("retired")));
    }

    [Fact]
    public async Task A_replacement_cut_short_still_uses_the_new_keys()
    {
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, (_) => Task.CompletedTask);
        Assert.Equal(0, Command("replace").Code);
        // as if it stopped after making the new keys, before retiring the old ones
        await Keys.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, Builders<BsonDocument>.Update.Unset("retired_at"));

        var kids = await KidsAsync();
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, async (client) => Assert.Equal([kids["signing:2"]], await JwksAsync(client)));
    }

    [Fact]
    public async Task Retired_keys_are_never_replaced_on_start()
    {
        await OnAsync(Host(), TestEnvironment.ApiSecret, null, (_) => Task.CompletedTask);
        await Keys.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, Builders<BsonDocument>.Update.Set("retired_at", DateTime.UtcNow));

        // new keys would end every session, so only the keys command makes them
        var error = await Assert.ThrowsAnyAsync<Exception>(() => OnAsync(Host(), TestEnvironment.ApiSecret, null, (_) => Task.CompletedTask));
        Assert.Contains("retired", error.ToString());
        Assert.Equal(2, await Keys.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public void Unknown_commands_change_nothing()
    {
        Assert.Equal(2, Command().Code);
        Assert.Equal(2, Command("rotate").Code);
        Assert.Equal(2, Command("replace", "now").Code);
    }
}
