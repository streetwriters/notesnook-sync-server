using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// Sessions exactly as IdentityServer4 stored them, restored into the database. Whatever replaces
/// IdentityServer4 must keep every one of them working as it does here, or users get logged out.
/// </summary>
public abstract class LegacyFixtureTests(IdentityHost host) : IdentityTestBase(host)
{
    private static string FixturePath([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path)!, "Fixtures", "legacy-sessions.json");

    private IMongoCollection<BsonDocument> Collection(string name) => host.Database.GetCollection<BsonDocument>(name);
    private IMongoCollection<BsonDocument> Grants => Collection(global::IdentityServer4.MongoDB.Constants.TableNames.PersistedGrant);

    private static BsonDocument Session(string email, string accessToken, string? refreshToken, params (string Name, string Token)[] others)
    {
        var session = new BsonDocument { ["email"] = email, ["access_token"] = accessToken, ["refresh_token"] = (BsonValue?)refreshToken ?? BsonNull.Value };
        foreach (var (name, token) in others) session[name] = token;
        return session;
    }

    /// <summary>
    /// Records the fixtures. Only meaningful on an IdentityServer4 build: run once with
    /// EXPORT_LEGACY_FIXTURES=1 and commit the result.
    /// </summary>
    [Fact]
    public async Task Export()
    {
        if (Environment.GetEnvironmentVariable("EXPORT_LEGACY_FIXTURES") != "1" || host.IsOpenIddict) return;

        var sessions = new BsonDocument();
        var emails = new List<string>();
        async Task<string> NewUserAsync(bool mfa = false)
        {
            var email = NewEmail();
            emails.Add(email);
            await SignupAsync(email);
            if (mfa) await EnableEmailMFAAsync(email);
            return email;
        }

        // logged in, never refreshed
        var email = await NewUserAsync();
        var (access, refresh) = await LoginAsync(email);
        sessions["fresh"] = Session(email, access, refresh);

        // refreshed once, so the replaced token still works until the new one is used
        email = await NewUserAsync();
        var (_, first) = await LoginAsync(email);
        (access, refresh) = await TokensAsync(await RefreshAsync(first));
        sessions["rotated"] = Session(email, access, refresh, ("replaced", first));

        // refreshed twice, so the first refresh token is gone
        email = await NewUserAsync();
        (_, first) = await LoginAsync(email);
        var (_, second) = await TokensAsync(await RefreshAsync(first));
        (access, refresh) = await TokensAsync(await RefreshAsync(second));
        sessions["settled"] = Session(email, access, refresh, ("first", first), ("second", second));

        // refreshed twice with the same token (lost responses), neither new token used yet
        email = await NewUserAsync();
        (_, first) = await LoginAsync(email);
        var (siblingAccess, sibling) = await TokensAsync(await RefreshAsync(first));
        (access, refresh) = await TokensAsync(await RefreshAsync(first));
        sessions["lost_response"] = Session(email, access, refresh, ("replaced", first), ("sibling_access", siblingAccess), ("sibling", sibling));

        // one user, two devices
        email = await NewUserAsync();
        (access, refresh) = await LoginAsync(email);
        var (otherAccess, otherRefresh) = await LoginAsync(email);
        sessions["two_devices"] = Session(email, access, refresh, ("other_access", otherAccess), ("other_refresh", otherRefresh));

        // logged in through email → mfa → mfa_password
        email = await NewUserAsync(mfa: true);
        var mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");
        var passwordToken = Token(await ReadAsync(await MFAGrantAsync(mfaToken, await EmailCodeAsync(email))), "access_token");
        (access, refresh) = await TokensAsync(await MFAPasswordGrantAsync(passwordToken, Password));
        sessions["mfa"] = Session(email, access, refresh);

        // signed up, tokens issued over WAMP
        email = NewEmail();
        emails.Add(email);
        var signup = await SignupAsync(email);
        sessions["signup"] = Session(email, signup.AccessToken, signup.RefreshToken);

        // logged out after a refresh, which leaves the refresh token alive
        email = await NewUserAsync();
        (_, first) = await LoginAsync(email);
        (access, refresh) = await TokensAsync(await RefreshAsync(first));
        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Post, "/account/logout", access)).StatusCode);
        sessions["logged_out_after_refresh"] = Session(email, access, refresh);

        // the short-lived access token of the password reset flow
        email = await NewUserAsync();
        var user = await UserAsync(email);
        var code = await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode"));
        var reset = await AccountAsync(HttpMethod.Post, "/account/token", null, new() { ["authorization_code"] = code, ["user_id"] = user.Id.ToString(), ["client_id"] = ClientId });
        sessions["password_reset"] = Session(email, Token(await ReadAsync(reset), "access_token"), null);

        var users = await Collection("users").Find(Builders<BsonDocument>.Filter.In("NormalizedEmail", emails.Select((e) => e.ToUpperInvariant()))).ToListAsync();
        var subjects = users.Select((u) => u["_id"].ToString()).ToList();
        var fixture = new BsonDocument
        {
            ["exported_at"] = DateTime.UtcNow,
            ["engine"] = "IdentityServer4 4.1.2",
            ["sessions"] = sessions,
            ["users"] = new BsonArray(users),
            ["roles"] = new BsonArray(await Collection("roles").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()),
            ["grants"] = new BsonArray(await Grants.Find(Builders<BsonDocument>.Filter.In("SubjectId", subjects)).ToListAsync()),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(FixturePath())!);
        await File.WriteAllTextAsync(FixturePath(), fixture.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson, Indent = true }) + "\n");
    }

    private sealed record Fixture(DateTime ExportedAt, Dictionary<string, BsonDocument> Sessions, List<BsonDocument> Grants);

    /// <summary>Puts the fixture's users and grants back as they were exported, and the clock just after the export.</summary>
    private async Task<Fixture> RestoreAsync(TimeSpan after)
    {
        var fixture = BsonDocument.Parse(await File.ReadAllTextAsync(FixturePath()));

        // the database may already have the client's role, under another id (users hold role ids as strings)
        var roleIds = new Dictionary<string, string>();
        foreach (var role in fixture["roles"].AsBsonArray.Select((r) => r.AsBsonDocument))
        {
            var existing = await Collection("roles").Find(new BsonDocument("NormalizedName", role["NormalizedName"])).FirstOrDefaultAsync();
            if (existing == null) await Collection("roles").InsertOneAsync(role);
            roleIds[role["_id"].ToString()!] = (existing ?? role)["_id"].ToString()!;
        }

        foreach (var user in fixture["users"].AsBsonArray.Select((u) => u.AsBsonDocument))
        {
            if (user.TryGetValue("Roles", out var roles) && roles.IsBsonArray)
                user["Roles"] = new BsonArray(roles.AsBsonArray.Select((id) => roleIds.GetValueOrDefault(id.ToString()!, id.ToString()!)));
            await Collection("users").ReplaceOneAsync(new BsonDocument("_id", user["_id"]), user, new ReplaceOptions { IsUpsert = true });
        }

        var subjects = fixture["users"].AsBsonArray.Select((u) => u["_id"].ToString()).ToList();
        await Grants.DeleteManyAsync(Builders<BsonDocument>.Filter.In("SubjectId", subjects));
        await Grants.InsertManyAsync(fixture["grants"].AsBsonArray.Select((g) => g.AsBsonDocument));

        var exportedAt = fixture["exported_at"].ToUniversalTime();
        host.Clock.Set(exportedAt + after);
        return new Fixture(exportedAt, fixture["sessions"].AsBsonDocument.ToDictionary((e) => e.Name, (e) => e.Value.AsBsonDocument), [.. fixture["grants"].AsBsonArray.Select((g) => g.AsBsonDocument)]);
    }

    private static string Get(Fixture fixture, string session, string token) => fixture.Sessions[session][token].AsString;

    private async Task<int> RefreshStatusAsync(Fixture fixture, string session, string token = "refresh_token") =>
        (int)(await RefreshAsync(Get(fixture, session, token))).StatusCode;

    [Fact]
    public async Task Restored_access_tokens_introspect_as_before()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        foreach (var session in fixture.Sessions.Keys.Order())
            Golden.AssertMatches($"legacy.{session}.introspection", await Golden.SnapshotAsync(await IntrospectAsync(Get(fixture, session, "access_token"))));
    }

    [Fact]
    public async Task Restored_access_tokens_work_on_the_account_api()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Get, "/account", Get(fixture, "fresh", "access_token"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Get, "/account", Get(fixture, "password_reset", "access_token"))).StatusCode);
    }

    /// <summary>The scope the apps hold for the session: that of the last token they were given, in its order.</summary>
    private static string ClientScope(Fixture fixture, string session)
    {
        var key = (Get(fixture, session, "refresh_token") + ":refresh_token").Sha256();
        var grant = fixture.Grants.First((g) => g["Key"].AsString == key);
        var data = System.Text.Json.Nodes.JsonNode.Parse(grant["Data"].AsString)!;
        return string.Join(" ", data["AccessToken"]!["Claims"]!.AsArray().Where((c) => c!["Type"]!.GetValue<string>() == "scope").Select((c) => c!["Value"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Restored_refresh_tokens_refresh_with_the_scope_the_apps_send()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        foreach (var session in new[] { "fresh", "mfa", "signup", "rotated", "lost_response" })
        {
            var response = await RefreshAsync(Get(fixture, session, "refresh_token"), ClientScope(fixture, session));
            Golden.AssertMatches($"legacy.{session}.refresh", await Golden.SnapshotAsync(response));
            var body = await ReadAsync(response);
            Assert.True(await IsActiveAsync(Token(body, "access_token")), session);
            await TokensAsync(await RefreshAsync(Token(body, "refresh_token"), Token(body, "scope")));
        }
    }

    [Fact]
    public async Task Restored_refresh_tokens_refresh()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        foreach (var session in new[] { "fresh", "mfa", "signup", "logged_out_after_refresh" })
        {
            var (access, refresh) = await TokensAsync(await RefreshAsync(Get(fixture, session, "refresh_token")));
            Assert.True(await IsActiveAsync(access), session);
            await TokensAsync(await RefreshAsync(refresh));
        }
    }

    [Fact]
    public async Task Restored_replaced_token_works_until_its_replacement_is_used()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "rotated", "replaced"));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "rotated"));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "rotated", "replaced"));
    }

    [Fact]
    public async Task Restored_settled_session_forgot_its_first_token()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "settled", "first"));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "settled", "second"));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "settled"));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "settled", "second"));
    }

    [Fact]
    public async Task Restored_lost_responses_resolve_on_first_use()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        Assert.True(await IsActiveAsync(Get(fixture, "lost_response", "sibling_access")));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "lost_response"));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "lost_response", "replaced"));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "lost_response", "sibling"));
    }

    [Fact]
    public async Task Restored_sessions_expire()
    {
        var fixture = await RestoreAsync(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(1));
        Assert.True(await IsActiveAsync(Get(fixture, "fresh", "access_token")));
        host.Clock.Set(fixture.ExportedAt + TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
        Assert.False(await IsActiveAsync(Get(fixture, "fresh", "access_token")));
        // signup tokens live an hour
        Assert.False(await IsActiveAsync(Get(fixture, "signup", "access_token")));

        host.Clock.Set(fixture.ExportedAt + TimeSpan.FromDays(45) - TimeSpan.FromHours(1));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "fresh"));
        host.Clock.Set(fixture.ExportedAt + TimeSpan.FromDays(45) + TimeSpan.FromHours(1));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "two_devices"));
    }

    [Fact]
    public async Task Restored_sessions_can_be_logged_out()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Post, "/account/logout", Get(fixture, "fresh", "access_token"))).StatusCode);
        Assert.False(await IsActiveAsync(Get(fixture, "fresh", "access_token")));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "fresh"));
    }

    [Fact]
    public async Task Restored_other_sessions_can_be_cleared()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        var response = await AccountAsync(HttpMethod.Post, "/account/sessions/clear?all=false", Get(fixture, "two_devices", "access_token"), new() { ["refresh_token"] = Get(fixture, "two_devices", "refresh_token") });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await IsActiveAsync(Get(fixture, "two_devices", "access_token")));
        Assert.False(await IsActiveAsync(Get(fixture, "two_devices", "other_access")));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "two_devices", "other_refresh"));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "two_devices"));
    }

    [Fact]
    public async Task Restored_sessions_end_when_the_user_is_deleted()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        var user = await UserAsync(fixture.Sessions["mfa"]["email"].AsString);
        await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().DeleteAsync(user));
        Assert.False(await IsActiveAsync(Get(fixture, "mfa", "access_token")));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "mfa"));
    }

    [Fact]
    public async Task Restored_tokens_are_counted()
    {
        // what tells when no session needs IdentityServer4's grants any more
        var hits = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Streetwriters.Identity.Legacy") listener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            lock (hits) hits.Add(string.Join(",", tags.ToArray().Select((t) => $"{t.Key}={t.Value}")));
        });
        listener.Start();

        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        Assert.True(await IsActiveAsync(Get(fixture, "fresh", "access_token")));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "fresh"));

        // only OpenIddict reads them through LegacyGrants; IdentityServer4 reads them as its own
        if (host.IsOpenIddict)
        {
            Assert.Contains("type=reference_token,outcome=valid", hits);
            Assert.Contains("type=refresh_token,outcome=valid", hits);
        }
        else Assert.Empty(hits);
    }

    // Sessions that carry on after the switch, whose tokens the new server issues

    [Fact]
    public async Task Restored_refresh_token_works_until_the_next_one_is_used()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        var (_, next) = await TokensAsync(await RefreshAsync(Get(fixture, "fresh", "refresh_token")));
        // the response was lost, so the client refreshes with the restored token again
        var (_, lost) = await TokensAsync(await RefreshAsync(Get(fixture, "fresh", "refresh_token")));

        var (access, after) = await TokensAsync(await RefreshAsync(next));
        Assert.True(await IsActiveAsync(access));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "fresh"));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(lost)).StatusCode);
        await TokensAsync(await RefreshAsync(after));
    }

    [Fact]
    public async Task Restored_refresh_token_can_be_revoked()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        var response = await client.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["token"] = Get(fixture, "two_devices", "refresh_token"),
            ["token_type_hint"] = "refresh_token"
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(400, await RefreshStatusAsync(fixture, "two_devices"));
        // as with IdentityServer4, all of the user's access tokens go, but not their other refresh tokens
        Assert.False(await IsActiveAsync(Get(fixture, "two_devices", "access_token")));
        Assert.False(await IsActiveAsync(Get(fixture, "two_devices", "other_access")));
        Assert.Equal(200, await RefreshStatusAsync(fixture, "two_devices", "other_refresh"));
    }

    [Fact]
    public async Task Restored_sessions_are_cleared_with_new_ones()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        var (access, refresh) = await TokensAsync(await RefreshAsync(Get(fixture, "two_devices", "refresh_token")));

        var response = await AccountAsync(HttpMethod.Post, "/account/sessions/clear?all=false", access, new() { ["refresh_token"] = refresh });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await IsActiveAsync(access));
        await TokensAsync(await RefreshAsync(refresh));
        Assert.False(await IsActiveAsync(Get(fixture, "two_devices", "other_access")));
        Assert.Equal(400, await RefreshStatusAsync(fixture, "two_devices", "other_refresh"));

        // the API evicts the restored token's cached introspection before the call returns
        Assert.Contains((Get(fixture, "two_devices", "other_access") + ":reference_token").Sha256(), host.NotesnookServer.ClearedTokens);
    }

    [Fact]
    public async Task Restored_session_refreshed_and_logged_out()
    {
        var fixture = await RestoreAsync(TimeSpan.FromMinutes(1));
        var (access, refresh) = await TokensAsync(await RefreshAsync(Get(fixture, "fresh", "refresh_token")));

        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Post, "/account/logout", access)).StatusCode);
        Assert.False(await IsActiveAsync(access));
        // IdentityServer4 left both refresh tokens working (see Logout_after_refresh_leaves_the_refresh_token_alive);
        // OpenIddict ends the session, and the restored one it came from
        // (the restored one first: using its replacement would end it on either server)
        var expected = host.IsOpenIddict ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
        Assert.Equal((int)expected, await RefreshStatusAsync(fixture, "fresh"));
        Assert.Equal(expected, (await RefreshAsync(refresh)).StatusCode);
    }
}

[Collection(IdentityServer4Collection.Name)]
public sealed class IdentityServer4LegacyFixtureTests(IdentityServer4Host host) : LegacyFixtureTests(host);

[Collection(OpenIddictCollection.Name)]
public sealed class OpenIddictLegacyFixtureTests(OpenIddictHost host) : LegacyFixtureTests(host);
