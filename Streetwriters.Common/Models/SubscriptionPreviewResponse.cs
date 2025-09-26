namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class SubscriptionPreviewResponse
    {
        [JsonPropertyName("data")]
        public SubscriptionPreviewData Data { get; set; }

        [JsonPropertyName("meta")]
        public Meta Meta { get; set; }
    }

    public partial class SubscriptionPreviewData
    {
        [JsonPropertyName("currency_code")]
        public string CurrencyCode { get; set; }

        [JsonPropertyName("billing_cycle")]
        public BillingCycle BillingCycle { get; set; }

        [JsonPropertyName("update_summary")]
        public UpdateSummary UpdateSummary { get; set; }

        [JsonPropertyName("immediate_transaction")]
        public TransactionV2 ImmediateTransaction { get; set; }

        [JsonPropertyName("next_transaction")]
        public TransactionV2 NextTransaction { get; set; }

        [JsonPropertyName("recurring_transaction_details")]
        public Details RecurringTransactionDetails { get; set; }
    }

    public partial class UpdateSummary
    {
        [JsonPropertyName("charge")]
        public UpdateSummaryItem Charge { get; set; }

        [JsonPropertyName("credit")]
        public UpdateSummaryItem Credit { get; set; }

        [JsonPropertyName("result")]
        public UpdateSummaryItem Result { get; set; }
    }

    public partial class UpdateSummaryItem
    {
        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }
    }
}
