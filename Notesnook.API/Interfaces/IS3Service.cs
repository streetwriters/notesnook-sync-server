using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;
using Notesnook.API.Models;
using Notesnook.API.Models.Responses;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Interfaces
{
    public interface IS3Service
    {
        Task DeleteObjectAsync(string userId, string name);
        Task DeleteDirectoryAsync(string userId);
        Task<long?> GetObjectSizeAsync(string userId, string name);
        string GetUploadObjectUrl(string userId, string name);
        string GetDownloadObjectUrl(string userId, string name);
        Task<MultipartUploadMeta> StartMultipartUploadAsync(string userId, string name, int parts, string uploadId = null);
        Task AbortMultipartUploadAsync(string userId, string name, string uploadId);
        Task CompleteMultipartUploadAsync(string userId, CompleteMultipartUploadRequest uploadRequest);
    }
}