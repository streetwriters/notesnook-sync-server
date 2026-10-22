/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

#if DEBUG
using System;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Util;
using Testcontainers.Minio;

namespace Notesnook.API.Helpers
{
    // Debug builds only: when no S3 is configured, runs a local S3 in Docker for as long as the API runs.
    // The fixed port keeps signed URLs valid across restarts and the volume keeps the files.
    public static class DevS3
    {
        private const int Port = 6264;
        private const string Bucket = "attachments";
        private const string Username = "minioadmin";
        private const string Password = "minioadmin";

        public static async Task<IAsyncDisposable?> StartIfNeededAsync()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("S3_SERVICE_URL"))) return null;

            // MinIO no longer publishes images; Silo is an API-compatible fork that does
            var container = new MinioBuilder("pgsty/silo:RELEASE.2026-09-16T00-00-00Z")
                .WithUsername(Username)
                .WithPassword(Password)
                .WithPortBinding(Port, MinioBuilder.MinioPort)
                .WithVolumeMount("notesnook-dev-s3", "/data")
                .Build();
            try
            {
                await container.StartAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not start the development S3 (is Docker running?), so attachments won't work: {ex.Message}");
                await container.DisposeAsync();
                return null;
            }

            var url = container.GetConnectionString();
            Environment.SetEnvironmentVariable("S3_SERVICE_URL", url);
            Environment.SetEnvironmentVariable("S3_REGION", "us-east-1");
            Environment.SetEnvironmentVariable("S3_ACCESS_KEY_ID", Username);
            Environment.SetEnvironmentVariable("S3_ACCESS_KEY", Password);
            Environment.SetEnvironmentVariable("S3_BUCKET_NAME", Bucket);

            using var client = new AmazonS3Client(Username, Password, new AmazonS3Config { ServiceURL = url, AuthenticationRegion = "us-east-1", ForcePathStyle = true });
            if (!await AmazonS3Util.DoesS3BucketExistV2Async(client, Bucket)) await client.PutBucketAsync(Bucket);

            Console.WriteLine($"Development S3 running at {url} (bucket \"{Bucket}\", user {Username}/{Password})");
            return container;
        }
    }
}
#endif
