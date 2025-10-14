using System;
using System.Text.Json.Serialization;

namespace Streetwriters.Common.Models
{
    public partial class ListPaymentsResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("response")]
        public Payment[]? Payments { get; set; }
    }

    public partial class Payment
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("subscription_id")]
        public long SubscriptionId { get; set; }

        [JsonPropertyName("amount")]
        public double Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("payout_date")]
        public string? PayoutDate { get; set; }

        [JsonPropertyName("is_paid")]
        public short IsPaid { get; set; }

        [JsonPropertyName("is_one_off_charge")]
        public bool IsOneOffCharge { get; set; }

        [JsonPropertyName("receipt_url")]
        public string? ReceiptUrl { get; set; }
    }
}
