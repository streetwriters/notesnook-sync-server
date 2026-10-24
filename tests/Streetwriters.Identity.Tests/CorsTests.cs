using System.Text.Json.Nodes;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// What a browser sees of Identity from another origin, as the web app calls it: whether it may send
/// requests, and read the responses (errors included, for the mfa_required data).
/// </summary>
public abstract class CorsTests(IdentityHost host) : IdentityTestBase(host)
{
    private const string Origin = "https://app.notesnook.com";

    private static JsonObject CorsOf(HttpResponseMessage response)
    {
        var headers = new JsonObject { ["status"] = (int)response.StatusCode };
        foreach (var (name, values) in response.Headers.Where((h) => h.Key.StartsWith("Access-Control-")).OrderBy((h) => h.Key, StringComparer.Ordinal))
            headers[name] = string.Join(", ", values);
        return headers;
    }

    private async Task<JsonObject> PreflightAsync(string path, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return CorsOf(await client.SendAsync(request));
    }

    private async Task<JsonObject> FromOriginAsync(HttpMethod method, string path, Dictionary<string, string>? form = null, string? bearer = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", Origin);
        if (form != null) request.Content = new FormUrlEncodedContent(form);
        if (bearer != null) request.Headers.Authorization = new("Bearer", bearer);
        return CorsOf(await client.SendAsync(request));
    }

    [Fact]
    public async Task Browsers_can_call_Identity()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, _) = await LoginAsync(email);
        await EnableEmailMFAAsync(email);

        var cors = new JsonObject
        {
            ["token.preflight"] = await PreflightAsync("/connect/token", "POST", "content-type"),
            ["token.preflight.bearer"] = await PreflightAsync("/connect/token", "POST", "authorization,content-type"),
            ["token"] = await FromOriginAsync(HttpMethod.Post, "/connect/token", new() { ["grant_type"] = "email", ["client_id"] = ClientId, ["email"] = email }),
            ["token.error"] = await FromOriginAsync(HttpMethod.Post, "/connect/token", new() { ["grant_type"] = "email", ["client_id"] = "nope", ["email"] = email }),
            ["token.mfa_required"] = await FromOriginAsync(HttpMethod.Post, "/connect/token", new() { ["grant_type"] = "password", ["client_id"] = ClientId, ["username"] = email, ["password"] = "wrong" }),
            ["introspect.preflight"] = await PreflightAsync("/connect/introspect", "POST", "authorization,content-type"),
            ["account.preflight"] = await PreflightAsync("/account", "GET", "authorization"),
            ["account"] = await FromOriginAsync(HttpMethod.Get, "/account", bearer: access),
            ["account.unauthorized"] = await FromOriginAsync(HttpMethod.Get, "/account", bearer: "nope"),
        };
        Golden.AssertMatches("cors", cors);
    }
}

[Collection(IdentityServer4Collection.Name)]
public sealed class IdentityServer4CorsTests(IdentityServer4Host host) : CorsTests(host);

[Collection(OpenIddictCollection.Name)]
public sealed class OpenIddictCorsTests(OpenIddictHost host) : CorsTests(host);
