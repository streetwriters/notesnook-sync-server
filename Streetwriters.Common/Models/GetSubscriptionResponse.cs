namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class GetSubscriptionResponse : PaddleResponse
    {
        [JsonPropertyName("data")]
        public Data Data { get; set; }
    }

    public partial class Data
    {
        // [JsonPropertyName("id")]
        // public string Id { get; set; }

        // [JsonPropertyName("status")]
        // public string Status { get; set; }

        [JsonPropertyName("customer_id")]
        public string CustomerId { get; set; }

        // [JsonPropertyName("address_id")]
        // public string AddressId { get; set; }

        // [JsonPropertyName("business_id")]
        // public object BusinessId { get; set; }

        // [JsonPropertyName("currency_code")]
        // public string CurrencyCode { get; set; }

        // [JsonPropertyName("created_at")]
        // public DateTimeOffset CreatedAt { get; set; }

        // [JsonPropertyName("updated_at")]
        // public DateTimeOffset UpdatedAt { get; set; }

        // [JsonPropertyName("started_at")]
        // public DateTimeOffset StartedAt { get; set; }

        [JsonPropertyName("first_billed_at")]
        public DateTimeOffset? FirstBilledAt { get; set; }

        // [JsonPropertyName("next_billed_at")]
        // public DateTimeOffset NextBilledAt { get; set; }

        // [JsonPropertyName("paused_at")]
        // public object PausedAt { get; set; }

        // [JsonPropertyName("canceled_at")]
        // public object CanceledAt { get; set; }

        // [JsonPropertyName("collection_mode")]
        // public string CollectionMode { get; set; }

        // [JsonPropertyName("billing_details")]
        // public object BillingDetails { get; set; }

        // [JsonPropertyName("current_billing_period")]
        // public CurrentBillingPeriod CurrentBillingPeriod { get; set; }

        [JsonPropertyName("billing_cycle")]
        public BillingCycle BillingCycle { get; set; }

        // [JsonPropertyName("scheduled_change")]
        // public object ScheduledChange { get; set; }

        // [JsonPropertyName("items")]
        // public Item[] Items { get; set; }

        // [JsonPropertyName("custom_data")]
        // public object CustomData { get; set; }

        [JsonPropertyName("management_urls")]
        public ManagementUrls ManagementUrls { get; set; }

        // [JsonPropertyName("discount")]
        // public object Discount { get; set; }

        // [JsonPropertyName("import_meta")]
        // public object ImportMeta { get; set; }
    }

    public partial class BillingCycle
    {
        [JsonPropertyName("frequency")]
        public long Frequency { get; set; }

        [JsonPropertyName("interval")]
        public string Interval { get; set; }
    }

    // public partial class CurrentBillingPeriod
    // {
    //     [JsonPropertyName("starts_at")]
    //     public DateTimeOffset StartsAt { get; set; }

    //     [JsonPropertyName("ends_at")]
    //     public DateTimeOffset EndsAt { get; set; }
    // }

    // public partial class Item
    // {
    //     [JsonPropertyName("status")]
    //     public string Status { get; set; }

    //     [JsonPropertyName("quantity")]
    //     public long Quantity { get; set; }

    //     [JsonPropertyName("recurring")]
    //     public bool Recurring { get; set; }

    //     [JsonPropertyName("created_at")]
    //     public DateTimeOffset CreatedAt { get; set; }

    //     [JsonPropertyName("updated_at")]
    //     public DateTimeOffset UpdatedAt { get; set; }

    //     [JsonPropertyName("previously_billed_at")]
    //     public DateTimeOffset PreviouslyBilledAt { get; set; }

    //     [JsonPropertyName("next_billed_at")]
    //     public DateTimeOffset NextBilledAt { get; set; }

    //     [JsonPropertyName("trial_dates")]
    //     public object TrialDates { get; set; }

    //     [JsonPropertyName("price")]
    //     public Price Price { get; set; }
    // }

    // public partial class Price
    // {
    //     [JsonPropertyName("id")]
    //     public string Id { get; set; }

    //     [JsonPropertyName("product_id")]
    //     public string ProductId { get; set; }

    //     [JsonPropertyName("type")]
    //     public string Type { get; set; }

    //     [JsonPropertyName("description")]
    //     public string Description { get; set; }

    //     [JsonPropertyName("name")]
    //     public string Name { get; set; }

    //     [JsonPropertyName("tax_mode")]
    //     public string TaxMode { get; set; }

    //     [JsonPropertyName("billing_cycle")]
    //     public BillingCycle BillingCycle { get; set; }

    //     [JsonPropertyName("trial_period")]
    //     public object TrialPeriod { get; set; }

    //     [JsonPropertyName("unit_price")]
    //     public UnitPrice UnitPrice { get; set; }

    //     [JsonPropertyName("unit_price_overrides")]
    //     public object[] UnitPriceOverrides { get; set; }

    //     [JsonPropertyName("custom_data")]
    //     public object CustomData { get; set; }

    //     [JsonPropertyName("status")]
    //     public string Status { get; set; }

    //     [JsonPropertyName("quantity")]
    //     public Quantity Quantity { get; set; }

    //     [JsonPropertyName("import_meta")]
    //     public object ImportMeta { get; set; }

    //     [JsonPropertyName("created_at")]
    //     public DateTimeOffset CreatedAt { get; set; }

    //     [JsonPropertyName("updated_at")]
    //     public DateTimeOffset UpdatedAt { get; set; }
    // }

    // public partial class Quantity
    // {
    //     [JsonPropertyName("minimum")]
    //     public long Minimum { get; set; }

    //     [JsonPropertyName("maximum")]
    //     public long Maximum { get; set; }
    // }

    // public partial class UnitPrice
    // {
    //     [JsonPropertyName("amount")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Amount { get; set; }

    //     [JsonPropertyName("currency_code")]
    //     public string CurrencyCode { get; set; }
    // }

    public partial class ManagementUrls
    {
        [JsonPropertyName("update_payment_method")]
        public Uri UpdatePaymentMethod { get; set; }

        [JsonPropertyName("cancel")]
        public Uri Cancel { get; set; }
    }
}
