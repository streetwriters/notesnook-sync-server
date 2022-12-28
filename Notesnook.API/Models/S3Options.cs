using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Notesnook.API.Models
{
    public class S3Options
    {
        public string ServiceUrl { get; set; }
        public string Region { get; set; }
        public string AccessKeyId { get; set; }
        public string SecretAccessKey { get; set; }
    }
}