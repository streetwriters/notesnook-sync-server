using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// Every request the apps make to Identity, exactly as packages/core sends them (api/token-manager.ts,
/// api/user-manager.ts, api/mfa-manager.ts, through utils/http.ts), with what they get back recorded.
/// </summary>
public abstract class AppRequestTests(IdentityHost host) : IdentityTestBase(host)
{
    // what user-manager.ts asks for when signing in
    private const string AppScope = "notesnook.sync offline_access IdentityServerApi";

    /// <summary>As http.ts: a form body without falsy values, and a bearer token.</summary>
    private Task<HttpResponseMessage> AppAsync(HttpMethod method, string path, Dictionary<string, object?>? data = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            // transformFormData leaves out undefined, null, false, 0 and ""
            var form = string.Join("&", (data ?? []).Where((p) => p.Value is not (null or false or 0 or "")).Select((p) =>
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value is bool b ? (b ? "true" : "false") : p.Value!.ToString()!)}"));
            request.Content = new StringContent(form, MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded"));
        }
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    /// <summary>Signs in as user-manager.ts does: email, then the emailed code, then the password.</summary>
    private async Task<JsonObject> SignInAsync(string email)
    {
        var step = await ReadAsync(await AppAsync(HttpMethod.Post, "/connect/token", new() { ["email"] = email, ["grant_type"] = "email", ["client_id"] = "notesnook" }));
        step = await ReadAsync(await AppAsync(HttpMethod.Post, "/connect/token", new()
        {
            ["grant_type"] = "mfa",
            ["client_id"] = "notesnook",
            ["mfa:code"] = await EmailCodeAsync(email),
            ["mfa:method"] = "email"
        }, Token(step, "access_token")));
        var response = await AppAsync(HttpMethod.Post, "/connect/token", new()
        {
            ["grant_type"] = "mfa_password",
            ["client_id"] = "notesnook",
            ["scope"] = AppScope,
            ["password"] = Password
        }, Token(step, "access_token"));
        Golden.AssertMatches("app.mfa_password", await Golden.SnapshotAsync(response));
        return await ReadAsync(response);
    }

    /// <summary>Random values (recovery codes, authenticator keys) are recorded by shape.</summary>
    private static async Task<JsonObject> ShapeAsync(HttpResponseMessage response)
    {
        var snapshot = await Golden.SnapshotAsync(response);
        snapshot["body"] = snapshot["body"] switch
        {
            JsonArray array => $"array of {array.Count}",
            JsonObject obj => new JsonArray([.. obj.Select((p) => JsonValue.Create(p.Key))]),
            var other => other?.DeepClone(),
        };
        return snapshot;
    }

    [Fact]
    public async Task Signing_in()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var session = await SignInAsync(email);
        Assert.True(await IsActiveAsync(Token(session, "access_token")));

        // with the password hash the app retries with when the first is rejected
        email = NewEmail();
        await SignupAsync(email);
        var step = await ReadAsync(await AppAsync(HttpMethod.Post, "/connect/token", new() { ["email"] = email, ["grant_type"] = "email", ["client_id"] = "notesnook" }));
        step = await ReadAsync(await AppAsync(HttpMethod.Post, "/connect/token", new() { ["grant_type"] = "mfa", ["client_id"] = "notesnook", ["mfa:code"] = await EmailCodeAsync(email), ["mfa:method"] = "email" }, Token(step, "access_token")));
        await Golden.AssertMatchesAsync("app.mfa_password.wrong_password", await AppAsync(HttpMethod.Post, "/connect/token", new()
        {
            ["grant_type"] = "mfa_password",
            ["client_id"] = "notesnook",
            ["scope"] = AppScope,
            ["password"] = "wrong-password"
        }, Token(step, "access_token")));
        await TokensAsync(await AppAsync(HttpMethod.Post, "/connect/token", new() { ["grant_type"] = "mfa_password", ["client_id"] = "notesnook", ["scope"] = AppScope, ["password"] = Password }, Token(step, "access_token")));
    }

    [Fact]
    public async Task Refreshing_and_signing_out()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var session = await SignInAsync(email);

        var response = await AppAsync(HttpMethod.Post, "/connect/token", new()
        {
            ["refresh_token"] = Token(session, "refresh_token"),
            ["grant_type"] = "refresh_token",
            ["scope"] = Token(session, "scope"),
            ["client_id"] = "notesnook"
        });
        Golden.AssertMatches("app.refresh", await Golden.SnapshotAsync(response));
        session = await ReadAsync(response);

        await Golden.AssertMatchesAsync("app.sessions", await AppAsync(HttpMethod.Get, "/account/sessions", token: Token(session, "access_token")));
        await Golden.AssertMatchesAsync("app.sessions.clear", await AppAsync(HttpMethod.Post, "/account/sessions/clear?all=false", new() { ["refresh_token"] = Token(session, "refresh_token") }, Token(session, "access_token")));
        await Golden.AssertMatchesAsync("app.logout", await AppAsync(HttpMethod.Post, "/account/logout", null, Token(session, "access_token")));
        Assert.False(await IsActiveAsync(Token(session, "access_token")));
        await Golden.AssertMatchesAsync("app.after_logout", await AppAsync(HttpMethod.Get, "/mfa/codes", token: Token(session, "access_token")));
    }

    [Fact]
    public async Task Account_settings()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var access = Token(await SignInAsync(email), "access_token");

        await Golden.AssertMatchesAsync("app.marketing_consent.off", await AppAsync(HttpMethod.Patch, "/account", new() { ["type"] = "change_marketing_consent", ["enabled"] = false }, access));
        await Golden.AssertMatchesAsync("app.marketing_consent.on", await AppAsync(HttpMethod.Patch, "/account", new() { ["type"] = "change_marketing_consent", ["enabled"] = true }, access));
        await Golden.AssertMatchesAsync("app.verify", await AppAsync(HttpMethod.Post, "/account/verify", new() { ["newEmail"] = null }, access));
        await Golden.AssertMatchesAsync("app.verify.new_email", await AppAsync(HttpMethod.Post, "/account/verify", new() { ["newEmail"] = NewEmail() }, access));
        await Golden.AssertMatchesAsync("app.change_email.wrong_code", await AppAsync(HttpMethod.Patch, "/account", new()
        {
            ["type"] = "change_email",
            ["new_email"] = NewEmail(),
            ["password"] = Password,
            ["verification_code"] = "000000"
        }, access));
        // debug and staging builds answer with the reset link, release builds email it
        var recover = await AppAsync(HttpMethod.Post, "/account/recover", new() { ["email"] = email, ["client_id"] = "notesnook" });
        Assert.Equal(HttpStatusCode.OK, recover.StatusCode);
        var link = await recover.Content.ReadAsStringAsync();
#if DEBUG
        Assert.Matches(@"/account/confirm\?userId=[0-9a-f]{24}&code=[^&]+&clientId=notesnook&type=RESET_PASSWORD$", link);
#else
        Assert.Empty(link);
        Assert.Contains(host.Emails.Calls, (c) => c.Method == nameof(Interfaces.ITemplatedEmailSender.SendPasswordResetEmailAsync) && ((string)c.Args[1]!).StartsWith("https://auth.example.com/account/confirm?"));
#endif
    }

    [Fact]
    public async Task Two_factor_settings()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var access = Token(await SignInAsync(email), "access_token");

        Golden.AssertMatches("app.mfa.setup.app", await ShapeAsync(await AppAsync(HttpMethod.Post, "/mfa", new() { ["type"] = "app", ["phoneNumber"] = null }, access)));
        await Golden.AssertMatchesAsync("app.mfa.setup.email", await AppAsync(HttpMethod.Post, "/mfa", new() { ["type"] = "email" }, access));
        await Golden.AssertMatchesAsync("app.mfa.enable.wrong_code", await AppAsync(HttpMethod.Patch, "/mfa", new() { ["type"] = "email", ["code"] = "000000", ["isFallback"] = false }, access));
        await Golden.AssertMatchesAsync("app.mfa.enable", await AppAsync(HttpMethod.Patch, "/mfa", new() { ["type"] = "email", ["code"] = await EmailCodeAsync(email), ["isFallback"] = false }, access));
        Golden.AssertMatches("app.mfa.codes", await ShapeAsync(await AppAsync(HttpMethod.Get, "/mfa/codes", token: access)));
        await Golden.AssertMatchesAsync("app.mfa.send", await AppAsync(HttpMethod.Post, "/mfa/send", new() { ["type"] = "email" }, access));
        // the app disables 2FA with DELETE /mfa, which the server doesn't serve
        await Golden.AssertMatchesAsync("app.mfa.disable", await AppAsync(HttpMethod.Delete, "/mfa", token: access));
    }

    [Fact]
    public async Task Health_check()
    {
        await Golden.AssertMatchesAsync("app.health", await AppAsync(HttpMethod.Get, "/health"));
    }
}

[Collection(IdentityServer4Collection.Name)]
public sealed class IdentityServer4AppRequestTests(IdentityServer4Host host) : AppRequestTests(host);

[Collection(OpenIddictCollection.Name)]
public sealed class OpenIddictAppRequestTests(OpenIddictHost host) : AppRequestTests(host);
