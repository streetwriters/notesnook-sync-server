namespace Notesnook.API.Models
{
    public class MultipartUploadMeta
    {
        public string UploadId { get; set; }
        public string[] Parts { get; set; }
    }
}