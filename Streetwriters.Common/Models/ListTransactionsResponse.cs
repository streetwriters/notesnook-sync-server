namespace Streetwriters.Common.Models
{
    using System;
    using System.Text.Json.Serialization;

    public partial class ListTransactionsResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("response")]
        public Transaction[] Transactions { get; set; }
    }

    public partial class Transaction
    {
        [JsonPropertyName("order_id")]
        public string OrderId { get; set; }

        [JsonPropertyName("checkout_id")]
        public string CheckoutId { get; set; }

        [JsonPropertyName("amount")]
        public string Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("passthrough")]
        public object Passthrough { get; set; }

        [JsonPropertyName("product_id")]
        public long ProductId { get; set; }

        [JsonPropertyName("is_subscription")]
        public bool IsSubscription { get; set; }

        [JsonPropertyName("is_one_off")]
        public bool IsOneOff { get; set; }

        [JsonPropertyName("subscription")]
        public PaddleSubscription Subscription { get; set; }

        [JsonPropertyName("user")]
        public PaddleTransactionUser User { get; set; }

        [JsonPropertyName("receipt_url")]
        public string ReceiptUrl { get; set; }
    }

    public partial class PaddleSubscription
    {
        [JsonPropertyName("subscription_id")]
        public long SubscriptionId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    public partial class PaddleTransactionUser
    {
        [JsonPropertyName("user_id")]
        public long UserId { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("marketing_consent")]
        public bool MarketingConsent { get; set; }
    }
}
