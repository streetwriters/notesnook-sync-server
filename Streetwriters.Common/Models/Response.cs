using System.Runtime.Serialization;
using Newtonsoft.Json;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Models
{
    public class Response : IResponse
    {
        [JsonIgnore]
        public bool Success { get; set; }
        public int StatusCode { get; set; }
    }
}
