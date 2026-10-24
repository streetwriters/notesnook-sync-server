using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Streetwriters.Identity.OpenIddictEngine;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// OpenIddict keeps what it stores about a session to what can still be used, as IdentityServer4 did,
/// however long the session lives.
/// </summary>
[Collection(OpenIddictCollection.Name)]
public sealed class TokenStorageTests(OpenIddictHost host) : IdentityTestBase(host)
{
    private Task<long> CountAsync(string collection, string field, string subject) =>
        host.Database.GetCollection<BsonDocument>(collection).CountDocumentsAsync(new BsonDocument(field, subject));

    private Task PruneAsync() => host.WithServicesAsync(async (services) => (await services.GetRequiredService<TokenPruner>().PruneAsync()).Tokens);

    [Fact]
    public async Task A_long_session_stays_small()
    {
        var email = NewEmail();
        var subject = (await SignupAsync(email)).UserId;
        var (_, refresh) = await LoginAsync(email);
        string previous = refresh, access = "";

        // a month of refreshing every six hours, with pruning twice a day
        for (var i = 0; i < 120; i++)
        {
            host.Clock.Advance(TimeSpan.FromHours(6));
            previous = refresh;
            (access, refresh) = await TokensAsync(await RefreshAsync(refresh));
            if (i % 2 == 1) await PruneAsync();
        }

        // the latest refresh token, the one it replaced, and a day's access tokens; the signup's session too
        Assert.InRange(await CountAsync("openiddict.tokens", "subject", subject), 1, 12);
        Assert.InRange(await CountAsync("openiddict.token_records", "Subject", subject), 1, 12);
        Assert.InRange(await CountAsync("openiddict.authorizations", "subject", subject), 1, 2);

        // and nothing that's still needed went: the current tokens, and the replaced one for a lost response
        Assert.True(await IsActiveAsync(access));
        var (_, lost) = await TokensAsync(await RefreshAsync(previous));
        await TokensAsync(await RefreshAsync(refresh));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(previous)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(lost)).StatusCode);
    }

    [Fact]
    public async Task Ended_sessions_go()
    {
        var email = NewEmail();
        var subject = (await SignupAsync(email)).UserId;
        var (access, refresh) = await LoginAsync(email);
        await TokensAsync(await RefreshAsync(refresh));
        Assert.Equal(HttpStatusCode.OK, (await AccountAsync(HttpMethod.Post, "/account/sessions/clear?all=true", access)).StatusCode);

        host.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromHours(1));
        await PruneAsync();
        Assert.Equal(0, await CountAsync("openiddict.tokens", "subject", subject));
        Assert.Equal(0, await CountAsync("openiddict.token_records", "Subject", subject));
        Assert.Equal(0, await CountAsync("openiddict.authorizations", "subject", subject));
    }

    [Fact]
    public async Task Sessions_left_alone_until_they_expire_are_kept()
    {
        var email = NewEmail();
        await SignupAsync(email);
        var (_, refresh) = await LoginAsync(email);

        host.Clock.Advance(TimeSpan.FromDays(44));
        await PruneAsync();
        await TokensAsync(await RefreshAsync(refresh));
    }
}
