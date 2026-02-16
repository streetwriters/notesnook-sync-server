using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Models
{
    public class SignupResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
        [JsonPropertyName("expires_in")]
        public int AccessTokenLifetime { get; set; }
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }
        [JsonPropertyName("scope")]
        public string Scope { get; set; }
        [JsonPropertyName("user_id")]
        public string UserId { get; set; }
        public string[]? Errors { get; set; }

        public static SignupResponse Error(IEnumerable<string> errors)
        {
            return new SignupResponse
            {
                Errors = [.. errors]
            };
        }
    }
}
