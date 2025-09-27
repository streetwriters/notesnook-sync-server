using System;
using System.Text.Json.Serialization;

namespace Streetwriters.Common.Models
{
    public partial class ListUsersResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("response")]
        public PaddleUser[] Users { get; set; }
    }

    public class PaddleUser
    {
        [JsonPropertyName("subscription_id")]
        public long SubscriptionId { get; set; }

        [JsonPropertyName("plan_id")]
        public long PlanId { get; set; }

        [JsonPropertyName("user_id")]
        public long UserId { get; set; }

        [JsonPropertyName("user_email")]
        public string UserEmail { get; set; }

        [JsonPropertyName("marketing_consent")]
        public bool MarketingConsent { get; set; }

        [JsonPropertyName("update_url")]
        public string UpdateUrl { get; set; }

        [JsonPropertyName("cancel_url")]
        public string CancelUrl { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("signup_date")]
        public string SignupDate { get; set; }

        [JsonPropertyName("quantity")]
        public long Quantity { get; set; }
    }
}
