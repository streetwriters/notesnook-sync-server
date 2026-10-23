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

/// <summary>Clients of the in-process Identity server, as the apps and the other servers use it.</summary>
public abstract class IdentityTestBase
{
    protected const string ClientId = "notesnook";
    protected const string Scopes = "notesnook.sync offline_access IdentityServerApi";
    protected const string Password = "password123";

    protected readonly IdentityHost host;
    protected readonly HttpClient client;

    protected IdentityTestBase(IdentityHost host)
    {
        this.host = host;
        host.Reset();
        client = host.CreateClient();
    }

    protected static string NewEmail() => $"user{Guid.NewGuid():N}@example.com";

    protected async Task<SignupResponse> SignupAsync(string email)
    {
        var response = await host.WithServicesAsync((services) => services.GetRequiredService<IUserAccountService>().CreateUserAsync(ClientId, email, Password));
        Assert.Null(response.Errors);
        return response;
    }

    protected Task<HttpResponseMessage> TokenAsync(Dictionary<string, string> form, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(form) };
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> PasswordGrantAsync(string email, string password, params (string Key, string Value)[] extra) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = ClientId,
            ["scope"] = Scopes,
            ["username"] = email,
            ["password"] = password,
        }.Concat(extra.Select((e) => KeyValuePair.Create(e.Key, e.Value))).ToDictionary());

    protected Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        TokenAsync(new() { ["grant_type"] = "refresh_token", ["client_id"] = ClientId, ["refresh_token"] = refreshToken });

    protected Task<HttpResponseMessage> EmailGrantAsync(string email, bool optionalMFA = false)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "email", ["client_id"] = ClientId, ["email"] = email };
        if (optionalMFA) form["mfa:optional"] = "true";
        return TokenAsync(form);
    }

    protected Task<HttpResponseMessage> MFAGrantAsync(string bearer, string code, string method = "email", bool optionalMFA = false)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "mfa", ["client_id"] = ClientId, ["mfa:code"] = code, ["mfa:method"] = method };
        if (optionalMFA) form["mfa:optional"] = "true";
        return TokenAsync(form, bearer);
    }

    protected Task<HttpResponseMessage> MFAPasswordGrantAsync(string bearer, string password) =>
        TokenAsync(new() { ["grant_type"] = "mfa_password", ["client_id"] = ClientId, ["password"] = password }, bearer);

    protected Task<HttpResponseMessage> IntrospectAsync(string token, string secret = TestEnvironment.ApiSecret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/introspect") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"notesnook:{secret}")));
        return client.SendAsync(request);
    }

    protected async Task<bool> IsActiveAsync(string token)
    {
        var response = await IntrospectAsync(token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["active"]!.GetValue<bool>();
    }

    protected Task<HttpResponseMessage> AccountAsync(HttpMethod method, string path, string? bearer, Dictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (form != null) request.Content = new FormUrlEncodedContent(form);
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client.SendAsync(request);
    }

    protected static async Task<JsonObject> ReadAsync(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonObject>())!;

    protected static async Task<(string AccessToken, string RefreshToken)> TokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        return (body["access_token"]!.GetValue<string>(), body["refresh_token"]!.GetValue<string>());
    }

    protected async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email) => await TokensAsync(await PasswordGrantAsync(email, Password));

    protected Task<User> UserAsync(string email) => host.FindUserAsync(email).ContinueWith((t) => t.Result ?? throw new Exception("No user " + email));

    protected async Task EnableEmailMFAAsync(string email)
    {
        var user = await UserAsync(email);
        await host.WithServicesAsync(async (services) => { await services.GetRequiredService<IMFAService>().EnableMFAAsync(user, MFAMethods.Email); return 0; });
    }

    protected async Task<string> EmailCodeAsync(string email)
    {
        var user = await UserAsync(email);
        return await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider));
    }

    protected async Task<string> RecoveryCodeAsync(string email)
    {
        var user = await UserAsync(email);
        var codes = await host.WithServicesAsync((services) => services.GetRequiredService<UserManager<User>>().GenerateNewTwoFactorRecoveryCodesAsync(user, 2));
        return codes!.First();
    }

    protected static string Token(JsonObject body, string key) => body[key]!.GetValue<string>();
}
