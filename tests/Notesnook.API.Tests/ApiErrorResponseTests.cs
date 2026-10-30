using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notesnook.API.Controllers;
using Notesnook.API.Interfaces;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using WampSharp.V2.Core.Contracts;

namespace Notesnook.API.Tests;

public class ApiErrorResponseTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private const string UserId = "user";

    private sealed class Api : IAsyncDisposable
    {
        private readonly IHost host;
        public HttpClient Client { get; }
        public ConcurrentQueue<(LogLevel Level, Exception? Exception)> Logs { get; } = new();

        public Api(TestDatabase db, IUserAccountService identity, IUserSubscriptionService? subscriptions = null, IS3Service? s3 = null)
        {
            var scope = db.CreateScope();
            var accessor = new WampServiceAccessor(Servers.NotesnookAPI) { UserAccountService = identity, UserSubscriptionService = subscriptions };
            s3 ??= Stub<IS3Service>.Create();

            host = new HostBuilder().ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(Logs)))
                .ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
                    services.AddApiExceptionHandler();
                    services.AddDefaultCors();
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
                    services.AddAuthorization(options => options.AddPolicy("Sync", policy => policy.RequireAuthenticatedUser()));
                    services.AddHttpContextAccessor();
                    services.AddHttpClient();
                    services.AddSingleton(accessor);
                    services.AddSingleton(s3);
                    services.AddSingleton(Stub<IUserKeysService>.Create());
                    services.AddSingleton<ISyncItemsRepositoryAccessor>(scope.Repositories);
                    services.AddSingleton<IUserService>((provider) => new UserService(
                        provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
                        scope.Repositories, scope.Unit, s3, scope.DbContext, accessor, NullLogger<UserService>.Instance));
                })
                .Configure(app =>
                {
                    app.UseExceptionHandler(_ => { });
                    app.UseCors("notesnook");
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).Start();
            Client = host.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", UserId)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<(LogLevel, Exception?)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(logs);
        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<(LogLevel, Exception?)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => logs.Enqueue((logLevel, exception));
        }
    }

    private static UserModel User => new() { UserId = UserId, Email = "user@example.com", MFA = new MFAConfig { PrimaryMethod = "app" } };

    private static Subscription FreeSubscription => new() { UserId = UserId, AppId = ApplicationType.NOTESNOOK, Plan = SubscriptionPlan.FREE };

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    [Fact]
    public async Task Deleting_with_a_wrong_password_is_a_400()
    {
        var deleted = false;
        var identity = Stub<IUserAccountService>.Create(
            ("VerifyPasswordAsync", (_) => Task.FromResult(false)),
            ("UnregisterUserAsync", (_) => { deleted = true; return Task.CompletedTask; }));
        await using var api = new Api(db, identity);

        var response = await api.Client.PostAsync("/users/delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = "wrong" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Wrong password.", await ErrorAsync(response));
        Assert.False(deleted);
    }

    [Fact]
    public async Task Uploading_past_the_storage_limit_is_a_400()
    {
        var subscriptions = Stub<IUserSubscriptionService>.Create(("GetUserSubscriptionAsync", (_) => Task.FromResult<Subscription?>(FreeSubscription)));
        var s3 = Stub<IS3Service>.Create(("TryReserveStorageAsync", (_) => Task.FromResult(false)));
        await using var api = new Api(db, Stub<IUserAccountService>.Create(), subscriptions, s3);

        var response = await api.Client.PutAsync("/s3?name=file", new ByteArrayContent(new byte[16]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Storage limit exceeded.", await ErrorAsync(response));
    }

    [Fact]
    public async Task Identity_being_down_is_a_503()
    {
        var identity = Stub<IUserAccountService>.Create(("GetUserAsync", (_) => throw new WampConnectionBrokenException("connection lost")));
        await using var api = new Api(db, identity);

        var response = await api.Client.GetAsync("/users");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.DoesNotContain("connection lost", await ErrorAsync(response));
    }

    [Fact]
    public async Task Browsers_can_read_error_responses_and_their_Retry_After()
    {
        var identity = Stub<IUserAccountService>.Create(("GetUserAsync", (_) => throw new WampConnectionBrokenException("connection lost")));
        await using var api = new Api(db, identity);

        var request = new HttpRequestMessage(HttpMethod.Get, "/users");
        request.Headers.Add("Origin", "https://app.notesnook.com");
        var response = await api.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("Retry-After", response.Headers.GetValues("Access-Control-Expose-Headers").Single());
    }

    [Fact]
    public async Task Internal_errors_are_a_500_logged_once_without_their_message()
    {
        // no user settings in the database, so GetUserAsync throws a plain Exception
        var identity = Stub<IUserAccountService>.Create(("GetUserAsync", (_) => Task.FromResult<UserModel?>(User)));
        var subscriptions = Stub<IUserSubscriptionService>.Create(("GetUserSubscriptionAsync", (_) => Task.FromResult<Subscription?>(FreeSubscription)));
        await using var api = new Api(db, identity, subscriptions);

        var response = await api.Client.GetAsync("/users");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("settings", await ErrorAsync(response));
        Assert.Single(api.Logs, (log) => log.Exception?.Message == "User settings not found.");
    }
}
