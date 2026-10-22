using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Exceptions;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Notesnook.API.Tests;

public class S3ServiceTests(TestDatabase db, MinioFixture minio) : IClassFixture<TestDatabase>, IClassFixture<MinioFixture>
{
    private const long MB = 1024 * 1024;
    private static readonly HttpClient Http = new();

    private sealed class HttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static IUserSubscriptionService FreePlan(string userId) => Stub<IUserSubscriptionService>.Create(
        ("GetUserSubscriptionAsync", (_) => Task.FromResult<Subscription?>(new Subscription { UserId = userId, AppId = ApplicationType.NOTESNOOK, Plan = SubscriptionPlan.FREE })));

    private S3Service CreateService(IUserSubscriptionService? subscriptions = null)
    {
        _ = minio;
        var accessor = new WampServiceAccessor(Servers.NotesnookAPI) { UserSubscriptionService = subscriptions };
        return new S3Service(new S3Clients(NullLogger<S3Service>.Instance), new HttpClients(), db.CreateScope().Repositories, accessor, NullLogger<S3Service>.Instance);
    }

    private static string NewUserId() => ObjectId.GenerateNewId().ToString();

    private static async Task UploadAsync(S3Service s3, string userId, string name, byte[] data)
    {
        var url = await s3.GetInternalUploadObjectUrlAsync(userId, name);
        using var response = await Http.PutAsync(url, new ByteArrayContent(data));
        response.EnsureSuccessStatusCode();
    }

    private async Task CreateSettingsAsync(string userId, long used)
    {
        await db.CreateScope().Repositories.UsersSettings.Collection.InsertOneAsync(new UserSettings
        {
            UserId = userId,
            Salt = "",
            StorageLimit = new Limit { Value = used, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
        });
    }

    private async Task<long> UsedAsync(string userId) =>
        (await db.CreateScope().Repositories.UsersSettings.Collection.Find((u) => u.UserId == userId).FirstAsync()).StorageLimit!.Value;

    private async Task<CompleteMultipartUploadRequest> UploadPartAsync(S3Service s3, string userId, string name, byte[] data)
    {
        var meta = await s3.StartMultipartUploadAsync(userId, name, 1);
        using var response = await Http.PutAsync(meta.Parts[0], new ByteArrayContent(data));
        response.EnsureSuccessStatusCode();
        return new CompleteMultipartUploadRequest
        {
            Key = name,
            UploadId = meta.UploadId,
            PartETags = [new PartETag(1, response.Headers.ETag!.Tag)]
        };
    }

    [Fact]
    public async Task Uploaded_objects_report_their_size()
    {
        var s3 = CreateService();
        var userId = NewUserId();

        await UploadAsync(s3, userId, "file", new byte[5]);

        Assert.Equal(5, await s3.GetObjectSizeAsync(userId, "file"));
        Assert.Equal(0, await s3.GetObjectSizeAsync(userId, "missing"));
    }

    [Fact]
    public async Task Deleting_an_object_removes_it()
    {
        var s3 = CreateService();
        var userId = NewUserId();
        await UploadAsync(s3, userId, "file", new byte[5]);

        await s3.DeleteObjectAsync(userId, "file");

        Assert.Equal(0, await s3.GetObjectSizeAsync(userId, "file"));
    }

    [Fact]
    public async Task Deleting_a_directory_removes_only_that_users_objects()
    {
        var s3 = CreateService();
        var userId = NewUserId();
        var otherUserId = NewUserId();
        foreach (var name in new[] { "a", "b", "c" }) await UploadAsync(s3, userId, name, new byte[5]);
        await UploadAsync(s3, otherUserId, "a", new byte[5]);

        await s3.DeleteDirectoryAsync(userId);

        foreach (var name in new[] { "a", "b", "c" }) Assert.Equal(0, await s3.GetObjectSizeAsync(userId, name));
        Assert.Equal(5, await s3.GetObjectSizeAsync(otherUserId, "a"));
    }

    [Fact]
    public async Task A_multipart_upload_within_the_limit_is_completed_and_charged()
    {
        var userId = NewUserId();
        var s3 = CreateService(FreePlan(userId));
        await CreateSettingsAsync(userId, used: 0);

        await s3.CompleteMultipartUploadAsync(userId, await UploadPartAsync(s3, userId, "file", new byte[5]));

        Assert.Equal(5, await s3.GetObjectSizeAsync(userId, "file"));
        Assert.Equal(5, await UsedAsync(userId));
    }

    [Fact]
    public async Task A_multipart_upload_past_the_limit_is_aborted_and_not_charged()
    {
        var userId = NewUserId();
        var s3 = CreateService(FreePlan(userId));
        // the free plan allows 50 MB a month
        await CreateSettingsAsync(userId, used: 50 * MB - 3);

        var error = await Assert.ThrowsAsync<ClientException>(async () => await s3.CompleteMultipartUploadAsync(userId, await UploadPartAsync(s3, userId, "file", new byte[5])));

        Assert.Equal("Storage limit reached.", error.Message);
        Assert.Equal(0, await s3.GetObjectSizeAsync(userId, "file"));
        Assert.Equal(50 * MB - 3, await UsedAsync(userId));
    }
}
