using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Models;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public class UserKeysServiceTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private static readonly long Minute = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static EncryptedData Key(string cipher) => new() { IV = "iv", Cipher = cipher, Salt = "salt" };

    private UserKeysService Service() => new(db.CreateScope().Repositories);

    // a user whose password change staged new keys at stagedAt and then failed
    private async Task<(string UserId, long Revision)> UserWithPendingKeysAsync(long stagedAt)
    {
        var userId = ObjectId.GenerateNewId().ToString();
        await db.CreateScope().Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings
        {
            UserId = userId,
            Salt = "",
            DataEncryptionKey = Key("old"),
            Revision = 1
        });
        Assert.Equal(KeyWriteResult.Success, await Service().StageKeysAsync(userId, new UserKeys { DataEncryptionKey = Key("new") }, 1, stagedAt));
        return (userId, 2);
    }

    private async Task<UserSettings> SettingsAsync(string userId) =>
        await db.CreateScope().Repositories.UsersSettings.Collection.Find((u) => u.UserId == userId).FirstAsync();

    [Fact]
    public async Task A_session_from_before_the_keys_were_staged_cant_commit_them()
    {
        var stagedAt = Now() - Minute;
        var (userId, revision) = await UserWithPendingKeysAsync(stagedAt);

        Assert.Equal(KeyWriteResult.LoginRequired, await Service().CommitPendingKeysAsync(userId, revision, stagedAt - Minute));

        var settings = await SettingsAsync(userId);
        Assert.Equal("old", settings.DataEncryptionKey!.Cipher);
        Assert.NotNull(settings.PendingKeys);
    }

    [Fact]
    public async Task A_login_after_the_keys_were_staged_commits_them()
    {
        var stagedAt = Now() - Minute;
        var (userId, revision) = await UserWithPendingKeysAsync(stagedAt);

        Assert.Equal(KeyWriteResult.Success, await Service().CommitPendingKeysAsync(userId, revision, stagedAt + 1000));

        var settings = await SettingsAsync(userId);
        Assert.Equal("new", settings.DataEncryptionKey!.Cipher);
        Assert.Null(settings.PendingKeys);
    }

    [Fact]
    public async Task A_session_from_before_the_keys_were_staged_cant_discard_them()
    {
        // long enough ago that the password change can't still be running
        var stagedAt = Now() - 10 * Minute;
        var (userId, revision) = await UserWithPendingKeysAsync(stagedAt);

        Assert.Equal(KeyWriteResult.LoginRequired, await Service().DiscardPendingKeysAsync(userId, revision, stagedAt - Minute));

        Assert.NotNull((await SettingsAsync(userId)).PendingKeys);
    }

    [Fact]
    public async Task A_login_after_the_keys_were_staged_discards_them_once_the_password_change_has_timed_out()
    {
        var stagedAt = Now() - 10 * Minute;
        var (userId, revision) = await UserWithPendingKeysAsync(stagedAt);

        Assert.Equal(KeyWriteResult.Success, await Service().DiscardPendingKeysAsync(userId, revision, stagedAt + 1000));

        var settings = await SettingsAsync(userId);
        Assert.Equal("old", settings.DataEncryptionKey!.Cipher);
        Assert.Null(settings.PendingKeys);
    }

    [Fact]
    public async Task A_login_after_the_keys_were_staged_cant_discard_them_while_the_password_change_may_still_be_running()
    {
        var stagedAt = Now() - Minute;
        var (userId, revision) = await UserWithPendingKeysAsync(stagedAt);

        Assert.Equal(KeyWriteResult.PasswordChangeInProgress, await Service().DiscardPendingKeysAsync(userId, revision, stagedAt + 1000));

        Assert.NotNull((await SettingsAsync(userId)).PendingKeys);
    }
}
