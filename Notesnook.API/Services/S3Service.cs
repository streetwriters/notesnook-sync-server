/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;

namespace Notesnook.API.Services
{
    public class S3Service : IS3Service
    {
        private readonly string BUCKET_NAME = "nn-attachments";
        private AmazonS3Client S3Client { get; }
        private HttpClient httpClient = new HttpClient();

        public S3Service(IOptions<S3Options> s3Options)
        {
            var config = new AmazonS3Config
            {
#if DEBUG
                ServiceURL = Servers.S3Server.ToString(),
#else
                ServiceURL = s3Options.Value.ServiceUrl,
                AuthenticationRegion = s3Options.Value.Region,
#endif
                ForcePathStyle = true,
                SignatureMethod = SigningAlgorithm.HmacSHA256,
                SignatureVersion = "4"
            };
#if DEBUG
            S3Client = new AmazonS3Client("S3RVER", "S3RVER", config);
#else
            S3Client = new AmazonS3Client(s3Options.Value.AccessKeyId, s3Options.Value.SecretAccessKey, config);
#endif
            AWSConfigsS3.UseSignatureVersion4 = true;
        }

        public async Task DeleteObjectAsync(string userId, string name)
        {
            var objectName = GetFullObjectName(userId, name);
            if (objectName == null) throw new Exception("Invalid object name."); ;

            var response = await S3Client.DeleteObjectAsync(BUCKET_NAME, objectName);

            if (!IsSuccessStatusCode(((int)response.HttpStatusCode)))
                throw new Exception("Could not delete object.");
        }

        public async Task DeleteDirectoryAsync(string userId)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = BUCKET_NAME,
                Prefix = userId,
            };

            var response = new ListObjectsV2Response();
            var keys = new List<KeyVersion>();
            do
            {
                response = await S3Client.ListObjectsV2Async(request);
                response.S3Objects.ForEach(obj => keys.Add(new KeyVersion
                {
                    Key = obj.Key,
                }));

                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);

            if (keys.Count <= 0) return;

            var deleteObjectsResponse = await S3Client
            .DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = BUCKET_NAME,
                Objects = keys,
            });

            if (!IsSuccessStatusCode((int)deleteObjectsResponse.HttpStatusCode))
                throw new Exception("Could not delete directory.");
        }

        public async Task<long?> GetObjectSizeAsync(string userId, string name)
        {
            var url = this.GetPresignedURL(userId, name, HttpVerb.HEAD);
            if (url == null) return null;

            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await httpClient.SendAsync(request);
            return response.Content.Headers.ContentLength;
        }


        public string GetUploadObjectUrl(string userId, string name)
        {
            var url = this.GetPresignedURL(userId, name, HttpVerb.PUT);
            if (url == null) return null;
            return url;
        }

        public string GetDownloadObjectUrl(string userId, string name)
        {
            var url = this.GetPresignedURL(userId, name, HttpVerb.GET);
            if (url == null) return null;
            return url;
        }

        public async Task<MultipartUploadMeta> StartMultipartUploadAsync(string userId, string name, int parts, string uploadId = null)
        {
            var objectName = GetFullObjectName(userId, name);
            if (userId == null || objectName == null) throw new Exception("Could not initiate multipart upload.");

            if (string.IsNullOrEmpty(uploadId))
            {
                var response = await S3Client.InitiateMultipartUploadAsync(BUCKET_NAME, objectName);
                if (!IsSuccessStatusCode(((int)response.HttpStatusCode))) throw new Exception("Failed to initiate multipart upload.");

                uploadId = response.UploadId;
            }

            var signedUrls = new string[parts];
            for (var i = 0; i < parts; ++i)
            {
                signedUrls[i] = GetPresignedURLForUploadPart(objectName, uploadId, i + 1);
            }

            return new MultipartUploadMeta
            {
                UploadId = uploadId,
                Parts = signedUrls
            };
        }

        public async Task AbortMultipartUploadAsync(string userId, string name, string uploadId)
        {
            var objectName = GetFullObjectName(userId, name);
            if (userId == null || objectName == null) throw new Exception("Could not abort multipart upload.");

            var response = await S3Client.AbortMultipartUploadAsync(BUCKET_NAME, objectName, uploadId);
            if (!IsSuccessStatusCode(((int)response.HttpStatusCode))) throw new Exception("Failed to abort multipart upload.");
        }

        public async Task CompleteMultipartUploadAsync(string userId, CompleteMultipartUploadRequest uploadRequest)
        {
            var objectName = GetFullObjectName(userId, uploadRequest.Key);
            if (userId == null || objectName == null) throw new Exception("Could not abort multipart upload.");

            uploadRequest.Key = objectName;
            uploadRequest.BucketName = BUCKET_NAME;
            var response = await S3Client.CompleteMultipartUploadAsync(uploadRequest);
            if (!IsSuccessStatusCode(((int)response.HttpStatusCode))) throw new Exception("Failed to complete multipart upload.");
        }

        private string GetPresignedURL(string userId, string name, HttpVerb httpVerb)
        {
            var objectName = GetFullObjectName(userId, name);
            if (userId == null || objectName == null) return null;

            var request = new GetPreSignedUrlRequest
            {
                BucketName = BUCKET_NAME,
                Expires = System.DateTime.Now.AddHours(1),
                Verb = httpVerb,
                Key = objectName,
#if DEBUG
                Protocol = Protocol.HTTP,
#else
                Protocol = Protocol.HTTPS,
#endif
            };
            return S3Client.GetPreSignedURL(request);
        }

        private string GetPresignedURLForUploadPart(string objectName, string uploadId, int partNumber)
        {
            return S3Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = BUCKET_NAME,
                Expires = System.DateTime.Now.AddHours(1),
                Verb = HttpVerb.PUT,
                Key = objectName,
                PartNumber = partNumber,
                UploadId = uploadId,
#if DEBUG
                Protocol = Protocol.HTTP,
#else
                Protocol = Protocol.HTTPS,
#endif
            });
        }

        private string GetFullObjectName(string userId, string name)
        {
            if (userId == null || !Regex.IsMatch(name, "[0-9a-zA-Z!" + Regex.Escape("-") + "_.*'()]")) return null;
            return $"{userId}/{name}";
        }

        bool IsSuccessStatusCode(int statusCode)
        {
            return ((int)statusCode >= 200) && ((int)statusCode <= 299);
        }
    }
}