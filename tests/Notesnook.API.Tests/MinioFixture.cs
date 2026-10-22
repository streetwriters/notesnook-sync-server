using Amazon.S3;
using Testcontainers.Minio;

namespace Notesnook.API.Tests;

/// <summary>
/// A throwaway MinIO-compatible S3 server in Docker, with S3Clients configured to use it.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    // StorageQuotaTests sets the same bucket name, so the two never disagree
    public const string Bucket = "test";

    // MinIO no longer publishes images; Silo is an API-compatible fork that does
    private readonly MinioContainer container = new MinioBuilder("pgsty/silo:RELEASE.2026-09-16T00-00-00Z").Build();

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        // S3Clients reads these when it's created; only S3ServiceTests uses this fixture
        Environment.SetEnvironmentVariable("S3_SERVICE_URL", container.GetConnectionString());
        Environment.SetEnvironmentVariable("S3_REGION", "us-east-1");
        Environment.SetEnvironmentVariable("S3_ACCESS_KEY_ID", container.GetAccessKey());
        Environment.SetEnvironmentVariable("S3_ACCESS_KEY", container.GetSecretKey());
        Environment.SetEnvironmentVariable("S3_BUCKET_NAME", Bucket);

        using var client = new AmazonS3Client(container.GetAccessKey(), container.GetSecretKey(), new AmazonS3Config
        {
            ServiceURL = container.GetConnectionString(),
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true
        });
        await client.PutBucketAsync(Bucket);
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
