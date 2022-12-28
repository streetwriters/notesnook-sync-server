using System.Text.Json.Serialization;

namespace Streetwriters.Common.Models
{
    public class UserModel
    {
        [JsonPropertyName("id")]
        public string UserId { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("phoneNumber")]
        public string PhoneNumber { get; set; }

        [JsonPropertyName("isEmailConfirmed")]
        public bool IsEmailConfirmed { get; set; }

        [JsonPropertyName("mfa")]
        public MFAConfig MFA { get; set; }
    }

}
