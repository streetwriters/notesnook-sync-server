using System.Reflection;
using AspNetCore.Identity.Mongo;
using AspNetCore.Identity.Mongo.Model;
using EphemeralMongo;
using IdentityServer4.ResponseHandling;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Tests;

public sealed class SignupTests : IDisposable
{
    private static readonly IMongoRunner Runner = StartRunner();

    // EphemeralMongo only stops mongod when the runner is disposed
    private static IMongoRunner StartRunner()
    {
        var runner = MongoRunner.Run(new MongoRunnerOptions { Version = MongoVersion.V8 });
        AppDomain.CurrentDomain.ProcessExit += (_, _) => runner.Dispose();
        return runner;
    }
    private readonly string databaseName = "identity_test_" + Guid.NewGuid().ToString("N");
    private readonly ServiceProvider services;

    static SignupTests()
    {
        // the self-hosted path skips the confirmation email and MFA setup
        Environment.SetEnvironmentVariable("SELF_HOSTED", "1");
    }

    public SignupTests()
    {
        var connectionString = new MongoUrlBuilder(Runner.ConnectionString) { DatabaseName = databaseName }.ToString();
        var collection = new MongoClient(Runner.ConnectionString).GetDatabase(databaseName).GetCollection<BsonDocument>("users");
        foreach (var field in new[] { "NormalizedEmail", "NormalizedUserName" })
            collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(new BsonDocument(field, 1), new CreateIndexOptions { Unique = true }));

        var collectionServices = new ServiceCollection().AddLogging();
        collectionServices.AddIdentityMongoDbProvider<User>(
            (options) =>
            {
                // as in Startup
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 0;
                options.User.RequireUniqueEmail = true;
            },
            (options) =>
            {
                options.RolesCollection = "roles";
                options.UsersCollection = "users";
                options.ConnectionString = connectionString;
            });
        services = collectionServices.BuildServiceProvider();
    }

    public void Dispose()
    {
        new MongoClient(Runner.ConnectionString).DropDatabase(databaseName);
        services.Dispose();
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }

    public class Stub<T> : DispatchProxy where T : class
    {
        private Dictionary<string, Func<object?[], object?>> handlers = [];

        public static T Create(params (string Method, Func<object?[], object?> Handler)[] handlers)
        {
            var proxy = Create<T, Stub<T>>();
            ((Stub<T>)(object)proxy).handlers = handlers.ToDictionary((h) => h.Method, (h) => h.Handler);
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => handlers.TryGetValue(method!.Name, out var handler) ? handler(args ?? []) : throw new NotImplementedException(method.Name);
    }

    private (UserAccountService Service, UserManager<User> Users) CreateService()
    {
        var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var tokens = Stub<ITokenGenerationService>.Create(("CreateUserTokensAsync", (_) => Task.FromResult<TokenResponse?>(new TokenResponse { AccessToken = "access" })));
        var service = new UserAccountService(
            users,
            Stub<IMFAService>.Create(),
            Stub<IPersistedGrantStore>.Create(),
            scope.ServiceProvider.GetRequiredService<RoleManager<MongoRole>>(),
            new EmailAddressValidator(new HttpClient(new FailingHandler()), NullLogger<EmailAddressValidator>.Instance),
            Stub<ITemplatedEmailSender>.Create(),
            tokens,
            NullLogger<UserAccountService>.Instance);
        return (service, users);
    }

    private long UserCount(string email) =>
        new MongoClient(Runner.ConnectionString).GetDatabase(databaseName).GetCollection<BsonDocument>("users")
            .CountDocuments(new BsonDocument("NormalizedEmail", email.ToUpperInvariant()));

    [Fact]
    public async Task Concurrent_signups_create_one_user()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select((_) => CreateService().Service.CreateUserAsync("notesnook", "same@example.com", "password123")));

        Assert.Single(results, (r) => r.UserId != null);
        Assert.All(results.Where((r) => r.UserId == null), (r) => Assert.Contains("Unable to create an account on this email.", r.Errors!));
        Assert.Equal(1, UserCount("same@example.com"));
    }

    [Fact]
    public async Task A_duplicate_the_validator_misses_is_rejected_by_the_index()
    {
        Assert.NotNull((await CreateService().Service.CreateUserAsync("notesnook", "race@example.com", "password123")).UserId);

        // as if a concurrent signup had passed Identity's duplicate check
        var (service, users) = CreateService();
        users.UserValidators.Clear();
        var result = await service.CreateUserAsync("notesnook", "race@example.com", "password123");

        Assert.Null(result.UserId);
        Assert.Equal(["Unable to create an account on this email."], result.Errors!);
        Assert.Equal(1, UserCount("race@example.com"));
    }
}
