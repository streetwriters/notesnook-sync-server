using System.Net;
using System.Text.Json.Nodes;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// Switching back to IdentityServer4 after running OpenIddict, and forward again, on one database:
/// no session may end because the server changed (see RollbackBridge).
/// </summary>
public sealed class RollbackDrillTests : IAsyncLifetime
{
    private readonly string database = "rollback_" + Guid.NewGuid().ToString("N");

    private sealed class Client(IdentityHost host) : IdentityTestBase(host)
    {
        public IdentityHost Host => host;
        public new Task<Common.Models.SignupResponse> SignupAsync(string email) => base.SignupAsync(email);
        public new Task<(string AccessToken, string RefreshToken)> LoginAsync(string email) => base.LoginAsync(email);
        public new Task<HttpResponseMessage> RefreshAsync(string refreshToken) => base.RefreshAsync(refreshToken);
        public new Task<HttpResponseMessage> IntrospectAsync(string token, string secret = TestEnvironment.ApiSecret) => base.IntrospectAsync(token, secret);
        public new Task<bool> IsActiveAsync(string token) => base.IsActiveAsync(token);
        public new Task<HttpResponseMessage> AccountAsync(HttpMethod method, string path, string? bearer, Dictionary<string, string>? form = null) => base.AccountAsync(method, path, bearer, form);
        public new static Task<(string AccessToken, string RefreshToken)> TokensAsync(HttpResponseMessage response) => IdentityTestBase.TokensAsync(response);
        public static string NewUserEmail() => NewEmail();
    }

    private async Task OnAsync(IdentityHost host, Func<Client, Task> steps)
    {
        await host.InitializeAsync();
        try { await steps(new Client(host)); }
        finally { await host.DisposeAsync(); }
    }

    private IdentityHost OpenIddict() => new OpenIddictHost { DatabaseName = database, KeepDatabase = true };
    private IdentityHost IdentityServer4() => new IdentityServer4Host { DatabaseName = database, KeepDatabase = true };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        var host = new IdentityServer4Host { DatabaseName = database };
        await host.Database.Client.DropDatabaseAsync(database);
    }

    [Fact]
    public async Task Sessions_survive_switching_back_and_forth()
    {
        var email = Client.NewUserEmail();
        string first = "", firstAccess = "", replaced = "", access = "", refresh = "", otherAccess = "", otherRefresh = "";

        await OnAsync(OpenIddict(), async (client) =>
        {
            await client.SignupAsync(email);
            (firstAccess, first) = await client.LoginAsync(email);
            (access, refresh) = await Client.TokensAsync(await client.RefreshAsync(first));
            replaced = first;
            (otherAccess, otherRefresh) = await client.LoginAsync(email);
        });

        string backAccess = "", backRefresh = "";
        await OnAsync(IdentityServer4(), async (client) =>
        {
            // what the API servers see is what IdentityServer4 answers for its own tokens
            var introspection = await Golden.SnapshotAsync(await client.IntrospectAsync(access));
            Assert.Equal(Golden.Read("password.introspection").ToJsonString(), introspection.ToJsonString());
            Assert.True(await client.IsActiveAsync(firstAccess));
            Assert.Equal(HttpStatusCode.OK, (await client.AccountAsync(HttpMethod.Get, "/account", access)).StatusCode);

            // the replaced refresh token still works until its replacement is used
            var (_, lost) = await Client.TokensAsync(await client.RefreshAsync(replaced));
            (backAccess, backRefresh) = await Client.TokensAsync(await client.RefreshAsync(refresh));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(replaced)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(lost)).StatusCode);

            // the other device's session is unaffected, and can still be ended
            await Client.TokensAsync(await client.RefreshAsync(otherRefresh));
            Assert.Equal(HttpStatusCode.OK, (await client.AccountAsync(HttpMethod.Post, "/account/logout", otherAccess)).StatusCode);
            Assert.False(await client.IsActiveAsync(otherAccess));
        });

        await OnAsync(OpenIddict(), async (client) =>
        {
            // and IdentityServer4's tokens from the rollback carry on after switching forward again
            Assert.True(await client.IsActiveAsync(backAccess));
            var (again, againRefresh) = await Client.TokensAsync(await client.RefreshAsync(backRefresh));
            Assert.True(await client.IsActiveAsync(again));
            await Client.TokensAsync(await client.RefreshAsync(againRefresh));
        });
    }

    [Fact]
    public async Task Copies_expire_with_their_sessions()
    {
        var email = Client.NewUserEmail();
        string access = "", refresh = "";
        await OnAsync(OpenIddict(), async (client) =>
        {
            await client.SignupAsync(email);
            (access, refresh) = await client.LoginAsync(email);
        });

        await OnAsync(IdentityServer4(), async (client) =>
        {
            client.Host.Clock.Advance(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
            Assert.False(await client.IsActiveAsync(access));
            client.Host.Clock.Advance(TimeSpan.FromDays(44));
            Assert.Equal(HttpStatusCode.OK, (await client.RefreshAsync(refresh)).StatusCode);
        });

        await OnAsync(IdentityServer4(), async (client) =>
        {
            var (_, again) = await client.LoginAsync(email);
            client.Host.Clock.Advance(TimeSpan.FromDays(46));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(again)).StatusCode);
        });
    }

    [Fact]
    public async Task Sessions_ended_on_OpenIddict_stay_ended()
    {
        var email = Client.NewUserEmail();
        string access = "", refresh = "", otherAccess = "", otherRefresh = "", revoked = "";
        await OnAsync(OpenIddict(), async (client) =>
        {
            await client.SignupAsync(email);
            (access, refresh) = await client.LoginAsync(email);
            (otherAccess, otherRefresh) = await client.LoginAsync(email);
            Assert.Equal(HttpStatusCode.OK, (await client.AccountAsync(HttpMethod.Post, "/account/sessions/clear?all=false", access, new() { ["refresh_token"] = refresh })).StatusCode);
            (_, revoked) = await client.LoginAsync(email);
            var response = await client.Host.CreateClient().PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = "notesnook", ["token"] = revoked }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });

        await OnAsync(IdentityServer4(), async (client) =>
        {
            Assert.False(await client.IsActiveAsync(otherAccess));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(otherRefresh)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(revoked)).StatusCode);
            await Client.TokensAsync(await client.RefreshAsync(refresh));
        });
    }

    [Fact]
    public async Task Logged_out_sessions_stay_logged_out()
    {
        var email = Client.NewUserEmail();
        string access = "", refresh = "", refreshed = "";
        await OnAsync(OpenIddict(), async (client) =>
        {
            await client.SignupAsync(email);
            (_, refresh) = await client.LoginAsync(email);
            (access, refreshed) = await Client.TokensAsync(await client.RefreshAsync(refresh));
            Assert.Equal(HttpStatusCode.OK, (await client.AccountAsync(HttpMethod.Post, "/account/logout", access)).StatusCode);
        });

        await OnAsync(IdentityServer4(), async (client) =>
        {
            Assert.False(await client.IsActiveAsync(access));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(refreshed)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(refresh)).StatusCode);
        });
    }

    [Fact]
    public async Task Replaced_refresh_tokens_stay_replaced()
    {
        var email = Client.NewUserEmail();
        string first = "", lost = "", next = "";
        await OnAsync(OpenIddict(), async (client) =>
        {
            await client.SignupAsync(email);
            (_, first) = await client.LoginAsync(email);
            var (_, second) = await Client.TokensAsync(await client.RefreshAsync(first));
            (_, lost) = await Client.TokensAsync(await client.RefreshAsync(first));
            (_, next) = await Client.TokensAsync(await client.RefreshAsync(second));
        });

        await OnAsync(IdentityServer4(), async (client) =>
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(first)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.RefreshAsync(lost)).StatusCode);
            await Client.TokensAsync(await client.RefreshAsync(next));
        });
    }
}
