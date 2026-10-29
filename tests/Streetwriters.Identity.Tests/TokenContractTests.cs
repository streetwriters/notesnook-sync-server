using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// The token contract clients and the other servers rely on, recorded as golden snapshots.
/// These must keep passing, unchanged, on whatever replaces IdentityServer4.
/// </summary>
public abstract class TokenContractTests(IdentityHost host) : IdentityTestBase(host)
{
    // Signup

    [Fact]
    public async Task Signup_issues_tokens()
    {
        var email = NewEmail();
        var signup = await SignupAsync(email);
        Golden.AssertMatches("signup", Golden.Normalize(JsonSerializer.SerializeToNode(signup), null)!);
        Golden.AssertMatches("signup.introspection", await Golden.SnapshotAsync(await IntrospectAsync(signup.AccessToken)));
        await TokensAsync(await RefreshAsync(signup.RefreshToken));
    }

    // Password grant

    [Fact]
    public async Task Password_grant()
    {
        var email = NewEmail();
        await SignupAsync(email);

        var response = await PasswordGrantAsync(email, Password);
        Golden.AssertMatches("password", await Golden.SnapshotAsync(response));
        var (access, _) = await TokensAsync(response);
        Golden.AssertMatches("password.introspection", await Golden.SnapshotAsync(await IntrospectAsync(access)));
    }

    [Fact]
    public async Task Password_grant_without_scope()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await Golden.AssertMatchesAsync("password.no_scope", await TokenAsync(new() { ["grant_type"] = "password", ["client_id"] = ClientId, ["username"] = email, ["password"] = Password }));
    }

    [Fact]
    public async Task Password_grant_with_openid_scope()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await Golden.AssertMatchesAsync("password.openid", await TokenAsync(new() { ["grant_type"] = "password", ["client_id"] = ClientId, ["scope"] = Scopes + " openid", ["username"] = email, ["password"] = Password }));
    }

    [Fact]
    public async Task Password_grant_rejects_wrong_password()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await Golden.AssertMatchesAsync("password.wrong_password", await PasswordGrantAsync(email, "wrong-password"));
    }

    [Fact]
    public async Task Password_grant_rejects_unknown_user()
    {
        await Golden.AssertMatchesAsync("password.unknown_user", await PasswordGrantAsync(NewEmail(), Password));
    }

    [Fact]
    public async Task Password_grant_locks_out()
    {
        var email = NewEmail();
        await SignupAsync(email);
        for (var i = 0; i < 5; i++) await PasswordGrantAsync(email, "wrong-password");
        await Golden.AssertMatchesAsync("password.locked_out", await PasswordGrantAsync(email, Password));
    }

    [Fact]
    public async Task Password_grant_requires_mfa()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);

        var required = await PasswordGrantAsync(email, Password);
        Golden.AssertMatches("password.mfa_required", await Golden.SnapshotAsync(required));
        await Golden.AssertMatchesAsync("password.mfa.wrong_code", await PasswordGrantAsync(email, Password, ("mfa:code", "000000"), ("mfa:method", "email")));
        await Golden.AssertMatchesAsync("password.mfa.invalid_method", await PasswordGrantAsync(email, Password, ("mfa:code", "000000"), ("mfa:method", "app")));
        await Golden.AssertMatchesAsync("password.mfa", await PasswordGrantAsync(email, Password, ("mfa:code", await EmailCodeAsync(email)), ("mfa:method", "email")));
        await Golden.AssertMatchesAsync("password.mfa.wrong_recovery_code", await PasswordGrantAsync(email, Password, ("mfa:code", "nope"), ("mfa:method", "recoveryCode")));
        await Golden.AssertMatchesAsync("password.mfa.recovery_code", await PasswordGrantAsync(email, Password, ("mfa:code", await RecoveryCodeAsync(email)), ("mfa:method", "recoveryCode")));
    }

    // Email → MFA → password grants

    [Fact]
    public async Task Email_mfa_password_grants()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);

        var emailResponse = await EmailGrantAsync(email);
        Golden.AssertMatches("email", await Golden.SnapshotAsync(emailResponse));
        var mfaToken = Token(await ReadAsync(emailResponse), "access_token");

        await Golden.AssertMatchesAsync("mfa.no_token", await TokenAsync(new() { ["grant_type"] = "mfa", ["client_id"] = ClientId, ["mfa:code"] = "000000", ["mfa:method"] = "email" }));
        await Golden.AssertMatchesAsync("mfa.wrong_code", await MFAGrantAsync(mfaToken, "000000"));
        await Golden.AssertMatchesAsync("mfa.invalid_method", await MFAGrantAsync(mfaToken, "000000", "sms"));
        // a step token is not an access token
        await Golden.AssertMatchesAsync("mfa.account", await AccountAsync(HttpMethod.Get, "/account", mfaToken));
        await Golden.AssertMatchesAsync("mfa.mfa_password_with_mfa_token", await MFAPasswordGrantAsync(mfaToken, Password));

        var mfaResponse = await MFAGrantAsync(mfaToken, await EmailCodeAsync(email));
        Golden.AssertMatches("mfa", await Golden.SnapshotAsync(mfaResponse));
        var passwordToken = Token(await ReadAsync(mfaResponse), "access_token");

        await Golden.AssertMatchesAsync("mfa_password.wrong_password", await MFAPasswordGrantAsync(passwordToken, "wrong-password"));
        await Golden.AssertMatchesAsync("mfa_password.mfa_with_password_token", await MFAGrantAsync(passwordToken, await EmailCodeAsync(email)));

        var tokens = await MFAPasswordGrantAsync(passwordToken, Password);
        Golden.AssertMatches("mfa_password", await Golden.SnapshotAsync(tokens));
        var (access, refresh) = await TokensAsync(tokens);
        Golden.AssertMatches("mfa_password.introspection", await Golden.SnapshotAsync(await IntrospectAsync(access)));
        await TokensAsync(await RefreshAsync(refresh));
    }

    [Fact]
    public async Task Email_grant_with_optional_mfa_skips_mfa()
    {
        var email = NewEmail();
        await SignupAsync(email);

        var response = await EmailGrantAsync(email, optionalMFA: true);
        Golden.AssertMatches("email.optional_mfa", await Golden.SnapshotAsync(response));
        var tokens = await MFAPasswordGrantAsync(Token(await ReadAsync(response), "access_token"), Password);
        Golden.AssertMatches("email.optional_mfa.mfa_password", await Golden.SnapshotAsync(tokens));
        await TokensAsync(tokens);
    }

    [Fact]
    public async Task Unregistered_users_cant_be_told_apart_from_registered_ones()
    {
        async Task<JsonArray> StepsAsync(string email, bool optionalMFA)
        {
            var response = await EmailGrantAsync(email, optionalMFA);
            var mfaToken = Token(await ReadAsync(response), "access_token");
            return
            [
                await Golden.SnapshotAsync(response),
                await Golden.SnapshotAsync(await MFAGrantAsync(mfaToken, "000000")),
                await Golden.SnapshotAsync(await MFAGrantAsync(mfaToken, "000000", "app")),
                await Golden.SnapshotAsync(await MFAGrantAsync(mfaToken, "nope", "recoveryCode")),
                await Golden.SnapshotAsync(await TokenAsync(new() { ["grant_type"] = "mfa", ["client_id"] = ClientId, ["mfa:method"] = "email" }, mfaToken)),
                await Golden.SnapshotAsync(await AccountAsync(HttpMethod.Post, "/mfa/send", mfaToken, new() { ["type"] = "email" })),
                // step tokens open nothing else
                await Golden.SnapshotAsync(await AccountAsync(HttpMethod.Get, "/account", mfaToken)),
                await Golden.SnapshotAsync(await AccountAsync(HttpMethod.Post, "/mfa", mfaToken, new() { ["type"] = "app" })),
            ];
        }

        foreach (var optionalMFA in new[] { false, true })
        {
            // signups get email MFA
            var email = NewEmail();
            await SignupAsync(email);
            await EnableEmailMFAAsync(email);

            var registered = await StepsAsync(email, optionalMFA);
            var unregistered = await StepsAsync(NewEmail(), optionalMFA);
            Assert.Equal(registered.ToJsonString(), unregistered.ToJsonString());
            Golden.AssertMatches(optionalMFA ? "email.unregistered.optional_mfa" : "email.unregistered", unregistered);
        }
    }

    [Fact]
    public async Task Mfa_send_accepts_step_token()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);
        var mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");

        await Golden.AssertMatchesAsync("mfa.send", await AccountAsync(HttpMethod.Post, "/mfa/send", mfaToken, new() { ["type"] = "email" }));
        Assert.Contains(host.Emails.Calls, (c) => c.Method == nameof(ITemplatedEmailSender.Send2FACodeEmailAsync));
    }

    private JsonArray EmailsSent() => new([.. host.Emails.Calls.Select((c) => JsonValue.Create(c.Method))]);

    private async Task LockOutAsync(string email)
    {
        var user = await UserAsync(email);
        await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task Mfa_grant_alerts_on_failures()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);
        var mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");

        await MFAGrantAsync(mfaToken, "000000");
        await MFAGrantAsync(mfaToken, "nope", "recoveryCode");
        Golden.AssertMatches("mfa.failures.emails", EmailsSent());
    }

    [Fact]
    public async Task Mfa_grant_accepts_recovery_codes()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);
        var mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");

        await Golden.AssertMatchesAsync("mfa.wrong_recovery_code", await MFAGrantAsync(mfaToken, "nope", "recoveryCode"));
        await Golden.AssertMatchesAsync("mfa.recovery_code", await MFAGrantAsync(mfaToken, await RecoveryCodeAsync(email), "recoveryCode"));
    }

    [Fact]
    public async Task Mfa_grant_rejects_locked_out_users()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);
        var mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");
        await LockOutAsync(email);

        await Golden.AssertMatchesAsync("mfa.locked_out", await MFAGrantAsync(mfaToken, await EmailCodeAsync(email)));
    }

    [Fact]
    public async Task Mfa_password_grant_locks_out()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var passwordToken = Token(await ReadAsync(await EmailGrantAsync(email, optionalMFA: true)), "access_token");

        for (var i = 0; i < 5; i++) await MFAPasswordGrantAsync(passwordToken, "wrong-password");
        await Golden.AssertMatchesAsync("mfa_password.locked_out", await MFAPasswordGrantAsync(passwordToken, Password));
        Golden.AssertMatches("mfa_password.failures.emails", EmailsSent());
    }

    [Fact]
    public async Task Mfa_grant_enables_mfa_for_old_clients()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");
        await TokensAsync(await MFAPasswordGrantAsync(Token(await ReadAsync(await MFAGrantAsync(mfaToken, await EmailCodeAsync(email))), "access_token"), Password));
        Assert.True((await UserAsync(email)).TwoFactorEnabled);

        email = NewEmail();
        await SignupAsync(email);
        mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");
        await TokensAsync(await MFAPasswordGrantAsync(Token(await ReadAsync(await MFAGrantAsync(mfaToken, await EmailCodeAsync(email), optionalMFA: true)), "access_token"), Password));
        Assert.False((await UserAsync(email)).TwoFactorEnabled);
    }

    [Fact]
    public async Task Successful_steps_reset_failed_attempts()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);

        var mfaToken = Token(await ReadAsync(await EmailGrantAsync(email)), "access_token");
        for (var i = 0; i < 3; i++) await MFAGrantAsync(mfaToken, "000000");
        Assert.Equal(3, (await UserAsync(email)).AccessFailedCount);
        var passwordToken = Token(await ReadAsync(await MFAGrantAsync(mfaToken, await EmailCodeAsync(email))), "access_token");
        Assert.Equal(0, (await UserAsync(email)).AccessFailedCount);

        for (var i = 0; i < 3; i++) await MFAPasswordGrantAsync(passwordToken, "wrong-password");
        Assert.Equal(3, (await UserAsync(email)).AccessFailedCount);
        await TokensAsync(await MFAPasswordGrantAsync(passwordToken, Password));
        Assert.Equal(0, (await UserAsync(email)).AccessFailedCount);

        for (var i = 0; i < 3; i++) await PasswordGrantAsync(email, Password, ("mfa:code", "000000"), ("mfa:method", "email"));
        Assert.Equal(3, (await UserAsync(email)).AccessFailedCount);
        await TokensAsync(await PasswordGrantAsync(email, Password, ("mfa:code", await EmailCodeAsync(email)), ("mfa:method", "email")));
        Assert.Equal(0, (await UserAsync(email)).AccessFailedCount);
    }

    [Fact]
    public async Task Password_grant_failures_dont_alert()
    {
        var email = NewEmail();
        await SignupAsync(email);
        await EnableEmailMFAAsync(email);
        await PasswordGrantAsync(email, "wrong-password", ("mfa:code", "000000"), ("mfa:method", "email"));
        await PasswordGrantAsync(email, Password, ("mfa:code", "000000"), ("mfa:method", "email"));
        Golden.AssertMatches("password.failures.emails", EmailsSent());
        await Golden.AssertMatchesAsync("password.mfa.wrong_password", await PasswordGrantAsync(email, "wrong-password", ("mfa:code", await EmailCodeAsync(email)), ("mfa:method", "email")));
    }

    // Refresh tokens

    [Fact]
    public async Task Refresh_rotates_tokens()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);

        var response = await RefreshAsync(refresh);
        Golden.AssertMatches("refresh", await Golden.SnapshotAsync(response));
        var (newAccess, newRefresh) = await TokensAsync(response);
        Assert.NotEqual(refresh, newRefresh);
        Assert.True(await IsActiveAsync(access));
        Assert.True(await IsActiveAsync(newAccess));
    }

    [Fact]
    public async Task Replaced_refresh_token_works_until_its_replacement_is_used()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (_, first) = await LoginAsync(email);

        // the client lost the responses
        var (_, second) = await TokensAsync(await RefreshAsync(first));
        var (_, third) = await TokensAsync(await RefreshAsync(first));
        Assert.NotEqual(second, third);

        var (_, fourth) = await TokensAsync(await RefreshAsync(third));
        await Golden.AssertMatchesAsync("refresh.replaced", await RefreshAsync(first));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(second)).StatusCode);
        await TokensAsync(await RefreshAsync(fourth));
    }

    [Fact]
    public async Task Concurrent_refreshes_all_succeed()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (_, refresh) = await LoginAsync(email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select((_) => RefreshAsync(refresh)));
        Assert.All(responses, (r) => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        // any of them can carry on
        await TokensAsync(await RefreshAsync((await TokensAsync(responses[^1])).RefreshToken));
    }

    [Fact]
    public async Task Refresh_token_expiry_slides()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (_, refresh) = await LoginAsync(email);

        host.Clock.Advance(TimeSpan.FromDays(44));
        (_, refresh) = await TokensAsync(await RefreshAsync(refresh));
        host.Clock.Advance(TimeSpan.FromDays(44));
        (_, refresh) = await TokensAsync(await RefreshAsync(refresh));
        host.Clock.Advance(TimeSpan.FromDays(46));
        await Golden.AssertMatchesAsync("refresh.expired", await RefreshAsync(refresh));
    }

    [Fact]
    public async Task Access_token_expires_after_six_hours()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, _) = await LoginAsync(email);

        host.Clock.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(1));
        Assert.True(await IsActiveAsync(access));
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await Golden.AssertMatchesAsync("introspection.expired", await IntrospectAsync(access));
    }

    [Fact]
    public async Task Refresh_with_the_scope_the_apps_send()
    {
        // packages/core's TokenManager refreshes with the scope of the token it has, and keeps the one it gets back
        var email = NewEmail();
        var signup = await SignupAsync(email);
        var response = await RefreshAsync(signup.RefreshToken, signup.Scope);
        Golden.AssertMatches("refresh.signup", await Golden.SnapshotAsync(response));
        var body = await ReadAsync(response);
        await TokensAsync(await RefreshAsync(Token(body, "refresh_token"), Token(body, "scope")));

        foreach (var (name, login) in new[] { ("refresh.client_scope", await PasswordGrantAsync(email, Password)), ("refresh.client_scope.no_scope", await TokenAsync(new() { ["grant_type"] = "password", ["client_id"] = ClientId, ["username"] = email, ["password"] = Password })) })
        {
            body = await ReadAsync(login);
            response = await RefreshAsync(Token(body, "refresh_token"), Token(body, "scope"));
            Golden.AssertMatches(name, await Golden.SnapshotAsync(response));
            body = await ReadAsync(response);
            await TokensAsync(await RefreshAsync(Token(body, "refresh_token"), Token(body, "scope")));
        }
    }

    [Fact]
    public async Task Refresh_with_other_scopes()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (_, refresh) = await LoginAsync(email);
        await Golden.AssertMatchesAsync("refresh.narrower_scope", await RefreshAsync(refresh, "notesnook.sync offline_access"));
        (_, refresh) = await LoginAsync(email);
        await Golden.AssertMatchesAsync("refresh.wider_scope", await RefreshAsync(refresh, "notesnook.sync offline_access IdentityServerApi openid mfa"));
        (_, refresh) = await LoginAsync(email);
        await Golden.AssertMatchesAsync("refresh.unknown_scope", await RefreshAsync(refresh, "notesnook.sync offline_access admin"));
    }

    [Fact]
    public async Task Refresh_rejects_unknown_token()
    {
        await Golden.AssertMatchesAsync("refresh.unknown", await RefreshAsync("ABCDEF"));
    }

    [Fact]
    public async Task Users_are_unregistered_without_their_password()
    {
        var email = NewEmail();
        var signup = await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);

        await host.WithServicesAsync(async (services) =>
        {
            var accounts = services.GetRequiredService<IUserAccountService>();
            // another client's user is refused, and one that doesn't exist is left alone
            await Assert.ThrowsAnyAsync<Exception>(() => accounts.UnregisterUserAsync("another-client", signup.UserId));
            Assert.NotNull(await services.GetRequiredService<UserManager<User>>().FindByIdAsync(signup.UserId));
            await accounts.UnregisterUserAsync(ClientId, MongoDB.Bson.ObjectId.GenerateNewId().ToString());

            await accounts.UnregisterUserAsync(ClientId, signup.UserId);
            Assert.Null(await services.GetRequiredService<UserManager<User>>().FindByIdAsync(signup.UserId));
            return 0;
        });

        await Golden.AssertMatchesAsync("deleted.introspection", await IntrospectAsync(access));
        await Golden.AssertMatchesAsync("deleted.refresh", await RefreshAsync(refresh));
    }

    [Fact]
    public async Task Deleted_users_tokens_stop_working_even_if_grants_remain()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);

        var user = await UserAsync(email);
        await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().DeleteAsync(user));

        Assert.False(await IsActiveAsync(access));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(refresh)).StatusCode);
    }

    // Introspection

    [Fact]
    public async Task Introspection_requires_the_api_secret()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, _) = await LoginAsync(email);
        await Golden.AssertMatchesAsync("introspection.wrong_secret", await IntrospectAsync(access, "wrong-secret"));
        await Golden.AssertMatchesAsync("introspection.unknown_token", await IntrospectAsync("ABCDEF"));
    }

    [Fact]
    public async Task Introspection_reports_email_verification()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var user = await UserAsync(email);
        await host.WithServicesAsync(async (services) =>
        {
            user.EmailConfirmed = false;
            return await services.GetRequiredService<UserManager<User>>().UpdateAsync(user);
        });
        var (access, _) = await LoginAsync(email);
        Golden.AssertMatches("introspection.unverified", await Golden.SnapshotAsync(await IntrospectAsync(access)));

        await host.WithServicesAsync(async (services) =>
        {
            var users = services.GetRequiredService<UserManager<User>>();
            user = (await users.FindByEmailAsync(email))!;
            // claims introspection removes from the user, though they stay in tokens already issued
            await users.AddClaimAsync(user, new System.Security.Claims.Claim("verified", "true"));
            return await users.AddClaimAsync(user, new System.Security.Claims.Claim("hcli", "x"));
        });
        (access, _) = await LoginAsync(email);
        Golden.AssertMatches("introspection.stale_claims", await Golden.SnapshotAsync(await IntrospectAsync(access)));
        Golden.AssertMatches("introspection.stale_claims.again", await Golden.SnapshotAsync(await IntrospectAsync(access)));
        (access, _) = await LoginAsync(email);
        Golden.AssertMatches("introspection.stale_claims.removed", await Golden.SnapshotAsync(await IntrospectAsync(access)));
    }

    // Account API

    [Fact]
    public async Task Account_api_accepts_access_tokens()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, _) = await LoginAsync(email);
        await Golden.AssertMatchesAsync("account", await AccountAsync(HttpMethod.Get, "/account", access));
        await Golden.AssertMatchesAsync("account.no_token", await AccountAsync(HttpMethod.Get, "/account", null));
        await Golden.AssertMatchesAsync("account.invalid_token", await AccountAsync(HttpMethod.Get, "/account", "ABCDEF"));
    }

    [Fact]
    public async Task Account_api_reads_the_scheme_in_any_case()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, _) = await LoginAsync(email);
        var statuses = new JsonObject();
        foreach (var scheme in new[] { "Bearer", "bearer", "BEARER" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/account");
            request.Headers.TryAddWithoutValidation("Authorization", $"{scheme} {access}");
            statuses[scheme] = (int)(await client.SendAsync(request)).StatusCode;
        }
        Golden.AssertMatches("account.scheme_case", statuses);
    }

    [Fact]
    public async Task Logout_revokes_the_session()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);
        var (otherAccess, otherRefresh) = await LoginAsync(email);

        await Golden.AssertMatchesAsync("logout", await AccountAsync(HttpMethod.Post, "/account/logout", access));
        Assert.False(await IsActiveAsync(access));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(refresh)).StatusCode);
        Assert.True(await IsActiveAsync(otherAccess));
        await TokensAsync(await RefreshAsync(otherRefresh));
    }

    [Fact]
    public async Task Logout_after_refresh_leaves_the_refresh_token_alive()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (_, refresh) = await LoginAsync(email);
        var (access, newRefresh) = await TokensAsync(await RefreshAsync(refresh));

        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Post, "/account/logout", access)).StatusCode);
        Assert.False(await IsActiveAsync(access));
        // IdentityServer4 behaviour, not intent: logout matches grants by the access token's jti, which a
        // refreshed refresh token doesn't hold. OpenIddict ends the whole session, which is only stricter.
        Assert.Equal(host.IsOpenIddict ? HttpStatusCode.BadRequest : HttpStatusCode.OK, (await RefreshAsync(newRefresh)).StatusCode);
    }

    [Fact]
    public async Task Clearing_other_sessions_keeps_the_current_one()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);
        var (otherAccess, otherRefresh) = await LoginAsync(email);

        await Golden.AssertMatchesAsync("sessions.clear", await AccountAsync(HttpMethod.Post, "/account/sessions/clear?all=false", access, new() { ["refresh_token"] = refresh }));
        Assert.True(await IsActiveAsync(access));
        await TokensAsync(await RefreshAsync(refresh));
        Assert.False(await IsActiveAsync(otherAccess));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(otherRefresh)).StatusCode);

        // the API servers cache introspection results under this key, and evict them on this message
        await WaitUntilAsync(() => !host.NotesnookServer.ClearCache.IsEmpty && !host.MessengerServer.ClearCache.IsEmpty);
        foreach (var server in new[] { host.NotesnookServer, host.MessengerServer })
        {
            var keys = server.ClearCache.SelectMany((m) => m.Keys).ToList();
            Assert.Contains((otherAccess + ":reference_token").Sha256(), keys);
            Assert.DoesNotContain((access + ":reference_token").Sha256(), keys);
        }
        await WaitUntilAsync(() => !host.MessengerServer.SSE.IsEmpty);
        Golden.AssertMatches("sessions.clear.sse", Golden.Normalize(JsonSerializer.SerializeToNode(host.MessengerServer.SSE.Single()), null)!);
    }

    [Fact]
    public async Task Clearing_all_sessions()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);
        var (otherAccess, _) = await LoginAsync(email);

        await Golden.AssertMatchesAsync("sessions.clear_all", await AccountAsync(HttpMethod.Post, "/account/sessions/clear?all=true", access, new() { ["refresh_token"] = refresh }));
        Assert.False(await IsActiveAsync(access));
        Assert.False(await IsActiveAsync(otherAccess));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(refresh)).StatusCode);
    }

    [Fact]
    public async Task Password_reset_code_grants_a_short_access_token()
    {
        var email = NewEmail();
        var signup = await SignupAsync(email);
        var user = await UserAsync(email);
        var code = await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode"));

        var form = new Dictionary<string, string> { ["authorization_code"] = code, ["user_id"] = signup.UserId, ["client_id"] = ClientId };
        await Golden.AssertMatchesAsync("account.token.invalid_code", await AccountAsync(HttpMethod.Post, "/account/token", null, new(form) { ["authorization_code"] = "nope" }));
        var response = await AccountAsync(HttpMethod.Post, "/account/token", null, form);
        Golden.AssertMatches("account.token", await Golden.SnapshotAsync(response));
        var access = Token(await ReadAsync(response), "access_token");
        Golden.AssertMatches("account.token.introspection", await Golden.SnapshotAsync(await IntrospectAsync(access)));
        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Get, "/account", access)).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_a_password_reset_token()
    {
        var email = NewEmail();
        var signup = await SignupAsync(email);
        var user = await UserAsync(email);
        var code = await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "PasswordResetAuthorizationCode"));
        var response = await AccountAsync(HttpMethod.Post, "/account/token", null, new() { ["authorization_code"] = code, ["user_id"] = signup.UserId, ["client_id"] = ClientId });
        var access = Token(await ReadAsync(response), "access_token");

        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Post, "/account/logout", access)).StatusCode);
        Assert.False(await IsActiveAsync(access));
    }

    // Protocol edges

    [Fact]
    public async Task Token_endpoint_errors()
    {
        await Golden.AssertMatchesAsync("token.unsupported_grant", await TokenAsync(new() { ["grant_type"] = "client_credentials", ["client_id"] = ClientId }));
        await Golden.AssertMatchesAsync("token.unknown_client", await TokenAsync(new() { ["grant_type"] = "password", ["client_id"] = "nope", ["username"] = "a@b.c", ["password"] = Password }));
        await Golden.AssertMatchesAsync("token.no_grant", await TokenAsync(new() { ["client_id"] = ClientId }));
        await Golden.AssertMatchesAsync("token.invalid_scope", await TokenAsync(new() { ["grant_type"] = "password", ["client_id"] = ClientId, ["scope"] = "admin", ["username"] = "a@b.c", ["password"] = Password }));
    }

    [Fact]
    public async Task Revocation_endpoint()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (access, refresh) = await LoginAsync(email);
        var (otherAccess, otherRefresh) = await LoginAsync(email);
        await Golden.AssertMatchesAsync("revocation", await client.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = ClientId, ["token"] = refresh, ["token_type_hint"] = "refresh_token" })));
        Golden.AssertMatches("revocation.effects", new JsonObject
        {
            ["refresh"] = (int)(await RefreshAsync(refresh)).StatusCode,
            ["access_active"] = await IsActiveAsync(access),
            ["other_access_active"] = await IsActiveAsync(otherAccess),
            ["other_refresh"] = (int)(await RefreshAsync(otherRefresh)).StatusCode,
        });
    }

    [Fact]
    public async Task Discovery_document()
    {
        var response = await client.GetAsync("/.well-known/openid-configuration");
        if (!host.IsOpenIddict)
        {
            await Golden.AssertMatchesAsync("discovery", response);
            return;
        }

        // each server describes itself differently; these are what the API servers' introspection
        // and the Bearer+jwt scheme read
        var expected = Golden.Read("discovery")["body"]!;
        var actual = await ReadAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected["issuer"]!.GetValue<string>().TrimEnd('/'), actual["issuer"]!.GetValue<string>().TrimEnd('/'));
        foreach (var endpoint in new[] { "token_endpoint", "introspection_endpoint", "revocation_endpoint" })
            Assert.Equal(new Uri(expected[endpoint]!.GetValue<string>()).AbsolutePath.TrimEnd('/'), new Uri(actual[endpoint]!.GetValue<string>()).AbsolutePath.TrimEnd('/'));
        var keys = await ReadAsync(await client.GetAsync(actual["jwks_uri"]!.GetValue<string>()));
        Assert.Contains(keys["keys"]!.AsArray(), (key) => key!["use"]?.GetValue<string>() == "sig");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition())
            await Task.Delay(50, cts.Token);
    }
}

[Collection(IdentityServer4Collection.Name)]
public sealed class IdentityServer4TokenContractTests(IdentityServer4Host host) : TokenContractTests(host);

[Collection(OpenIddictCollection.Name)]
public sealed class OpenIddictTokenContractTests(OpenIddictHost host) : TokenContractTests(host);
