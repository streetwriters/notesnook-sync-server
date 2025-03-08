using System.Collections.Generic;
using Amazon.S3.Model;

namespace Notesnook.API.Models;

public class CompleteMultipartUploadRequestWrapper
{
    public string Key { get; set; }
    public List<PartETagWrapper> PartETags { get; set; }
    public string UploadId { get; set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public CompleteMultipartUploadRequestWrapper()
    {
    }

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