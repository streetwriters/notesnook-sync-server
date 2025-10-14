using System.Collections.Generic;
using Amazon.S3.Model;

namespace Notesnook.API.Models;

public class CompleteMultipartUploadRequestWrapper
{
    public required string Key { get; set; }
    public required List<PartETagWrapper> PartETags { get; set; }
    public required string UploadId { get; set; }

    public CompleteMultipartUploadRequest ToRequest()
    {
        CompleteMultipartUploadRequest completeMultipartUploadRequest = new CompleteMultipartUploadRequest();
        completeMultipartUploadRequest.Key = Key;
        completeMultipartUploadRequest.UploadId = UploadId;
        completeMultipartUploadRequest.PartETags = [];
        foreach (var partETagWrapper in PartETags)
        {
            var partETag = new PartETag
            {
                PartNumber = partETagWrapper.PartNumber,
                ETag = partETagWrapper.ETag
            };
            completeMultipartUploadRequest.PartETags.Add(partETag);
        }

        return completeMultipartUploadRequest;
    }
}