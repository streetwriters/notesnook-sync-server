using System.Text.Json.Serialization;
using Streetwriters.Common.Models;

namespace Notesnook.API.Models.Responses
{
    public class SignupResponse : Response
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("errors")]
        public string[] Errors { get; set; }
    }
}
