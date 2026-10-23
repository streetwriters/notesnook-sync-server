using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using IdentityModel.AspNetCore.OAuth2Introspection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Streetwriters.Common;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// The API servers' view of Identity's tokens, through the introspection client and options they use
/// (Notesnook.API and Streetwriters.Messenger Startup), and the claims their authorization checks.
/// </summary>
public abstract class ApiIntrospectionTests(IdentityHost host) : IdentityTestBase(host), IAsyncLifetime
{
    private IHost api = null!;
    private HttpClient apiClient = null!;

    public async Task InitializeAsync()
    {
        api = new HostBuilder()
            .ConfigureWebHost((builder) => builder
                .UseTestServer()
                .ConfigureServices((services) =>
                {
                    services.AddRouting();
                    services.AddDistributedMemoryCache();
                    services.AddAuthentication()
                        .AddOAuth2Introspection("introspection", (options) =>
                        {
                            // as the API servers configure it
                            options.Authority = Servers.IdentityServer.ToString();
                            options.ClientSecret = Constants.NOTESNOOK_API_SECRET;
                            options.ClientId = "notesnook";
                            options.DiscoveryPolicy.RequireHttps = false;
                            options.CacheKeyGenerator = (options, token) => (token + ":" + "reference_token").Sha256();
                            options.SaveToken = true;
                            options.EnableCaching = true;
                            options.CacheDuration = TimeSpan.FromMinutes(30);
                        });
                    // the introspection client reaches Identity through its own HttpClient
                    services.AddHttpClient(OAuth2IntrospectionDefaults.BackChannelHttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => host.Host.GetTestServer().CreateHandler());
                })
                .Configure((app) =>
                {
                    app.UseRouting();
                    app.UseEndpoints((endpoints) => endpoints.MapGet("/claims", async (HttpContext context) =>
                    {
                        var result = await context.AuthenticateAsync("introspection");
                        if (!result.Succeeded) return Results.Unauthorized();
                        var user = result.Principal;
                        var claims = new JsonObject();
                        foreach (var group in user.Claims.GroupBy((c) => c.Type).OrderBy((g) => g.Key, StringComparer.Ordinal))
                            claims[group.Key] = new JsonArray([.. group.Select((c) => JsonValue.Create(c.Value)).OrderBy((v) => v!.ToString(), StringComparer.Ordinal)]);
                        return Results.Json(new
                        {
                            claims,
                            // NotesnookUserRequirement and SyncRequirement
                            notesnookUser = user.HasClaim("aud", "notesnook") && user.HasClaim("role", "notesnook"),
                            sync = user.FindFirstValue("sub") != null && user.HasClaim("scope", "notesnook.sync") && user.HasClaim("verified", "true"),
                        });
                    }));
                }))
            .Build();
        await api.StartAsync();
        apiClient = api.GetTestServer().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await api.StopAsync();
        api.Dispose();
    }

    private Task<HttpResponseMessage> ClaimsAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/claims");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return apiClient.SendAsync(request);
    }

    [Fact]
    public async Task Sessions_are_authorized()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);
        Golden.AssertMatches("api.claims", await Golden.SnapshotAsync(await ClaimsAsync(access)));

        var (refreshed, _) = await TokensAsync(await RefreshAsync(refresh));
        Golden.AssertMatches("api.claims", await Golden.SnapshotAsync(await ClaimsAsync(refreshed)));
    }

    [Fact]
    public async Task Signup_tokens_are_authorized()
    {
        var signup = await SignupAsync(NewEmail());
        Golden.AssertMatches("api.claims.signup", await Golden.SnapshotAsync(await ClaimsAsync(signup.AccessToken)));
    }

    [Fact]
    public async Task Other_tokens_are_not()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);
        var stepToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClaimsAsync(stepToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClaimsAsync("ABCDEF")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClaimsAsync(new string('A', 64))).StatusCode);
    }

    [Fact]
    public async Task Ended_sessions_are_not()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, _) = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Post, "/account/logout", access)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClaimsAsync(access)).StatusCode);
    }
}

[Collection(IdentityServer4Collection.Name)]
public sealed class IdentityServer4ApiIntrospectionTests(IdentityServer4Host host) : ApiIntrospectionTests(host);

[Collection(OpenIddictCollection.Name)]
public sealed class OpenIddictApiIntrospectionTests(OpenIddictHost host) : ApiIntrospectionTests(host);
