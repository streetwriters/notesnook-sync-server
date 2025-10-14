namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class ListTransactionsResponseV2 : PaddleResponse
    {
        [JsonPropertyName("data")]
        public TransactionV2[]? Transactions { get; set; }
    }

    public partial class TransactionV2
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("customer_id")]
        public string? CustomerId { get; set; }

        // [JsonPropertyName("address_id")]
        // public string AddressId { get; set; }

        // [JsonPropertyName("business_id")]
        // public object BusinessId { get; set; }

        [JsonPropertyName("custom_data")]
        public Dictionary<string, string>? CustomData { get; set; }

        [JsonPropertyName("origin")]
        public string? Origin { get; set; }

        // [JsonPropertyName("collection_mode")]
        // public string CollectionMode { get; set; }

        // [JsonPropertyName("subscription_id")]
        // public string SubscriptionId { get; set; }

        // [JsonPropertyName("invoice_id")]
        // public string InvoiceId { get; set; }

        // [JsonPropertyName("invoice_number")]
        // public string InvoiceNumber { get; set; }

        [JsonPropertyName("billing_details")]
        public BillingDetails? BillingDetails { get; set; }

        [JsonPropertyName("billing_period")]
        public BillingPeriod? BillingPeriod { get; set; }

        // [JsonPropertyName("currency_code")]
        // public string CurrencyCode { get; set; }

        // [JsonPropertyName("discount_id")]
        // public string DiscountId { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        // [JsonPropertyName("updated_at")]
        // public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("billed_at")]
        public DateTimeOffset? BilledAt { get; set; }

        [JsonPropertyName("items")]
        public Item[]? Items { get; set; }

        [JsonPropertyName("details")]
        public Details? Details { get; set; }

        // [JsonPropertyName("payments")]
        // public Payment[] Payments { get; set; }

        // [JsonPropertyName("checkout")]
        // public Checkout Checkout { get; set; }
    }

    public partial class BillingDetails
    {
        // [JsonPropertyName("enable_checkout")]
        // public bool EnableCheckout { get; set; }

        [JsonPropertyName("payment_terms")]
        public PaymentTerms? PaymentTerms { get; set; }

        // [JsonPropertyName("purchase_order_number")]
        // public string PurchaseOrderNumber { get; set; }

        // [JsonPropertyName("additional_information")]
        // public object AdditionalInformation { get; set; }
    }

    public partial class PaymentTerms
    {
        [JsonPropertyName("interval")]
        public string? Interval { get; set; }

        [JsonPropertyName("frequency")]
        public long Frequency { get; set; }
    }

    public partial class BillingPeriod
    {
        [JsonPropertyName("starts_at")]
        public DateTimeOffset StartsAt { get; set; }

        [JsonPropertyName("ends_at")]
        public DateTimeOffset EndsAt { get; set; }
    }

    // public partial class Checkout
    // {
    //     [JsonPropertyName("url")]
    //     public Uri Url { get; set; }
    // }

    public partial class Details
    {
        // [JsonPropertyName("tax_rates_used")]
        // public TaxRatesUsed[] TaxRatesUsed { get; set; }

        [JsonPropertyName("totals")]
        public Totals? Totals { get; set; }

        // [JsonPropertyName("adjusted_totals")]
        // public AdjustedTotals AdjustedTotals { get; set; }

        // [JsonPropertyName("payout_totals")]
        // public Dictionary<string, string> PayoutTotals { get; set; }

        // [JsonPropertyName("adjusted_payout_totals")]
        // public AdjustedTotals AdjustedPayoutTotals { get; set; }

        [JsonPropertyName("line_items")]
        public LineItem[]? LineItems { get; set; }
    }

    public partial class Totals
    {
        [JsonPropertyName("subtotal")]
        public long Subtotal { get; set; }

        [JsonPropertyName("tax")]
        public long Tax { get; set; }

        [JsonPropertyName("discount")]
        public long Discount { get; set; }

        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("grand_total")]
        public long GrandTotal { get; set; }

        // [JsonPropertyName("fee")]
        // public object Fee { get; set; }

        // [JsonPropertyName("credit")]
        // public long Credit { get; set; }

        // [JsonPropertyName("credit_to_balance")]
        // public long CreditToBalance { get; set; }

        [JsonPropertyName("balance")]
        public long Balance { get; set; }

        // [JsonPropertyName("earnings")]
        // public object Earnings { get; set; }

        [JsonPropertyName("currency_code")]
        public string? CurrencyCode { get; set; }
    }
    // public partial class AdjustedTotals
    // {
    //     [JsonPropertyName("subtotal")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Subtotal { get; set; }

    //     [JsonPropertyName("tax")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Tax { get; set; }

    //     [JsonPropertyName("total")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Total { get; set; }

    //     [JsonPropertyName("fee")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Fee { get; set; }

    //     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    //     [JsonPropertyName("chargeback_fee")]
    //     public ChargebackFee ChargebackFee { get; set; }

    //     [JsonPropertyName("earnings")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Earnings { get; set; }

    //     [JsonPropertyName("currency_code")]
    //     public string CurrencyCode { get; set; }

    //     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    //     [JsonPropertyName("grand_total")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long? GrandTotal { get; set; }
    // }

    // public partial class ChargebackFee
    // {
    //     [JsonPropertyName("amount")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Amount { get; set; }

    //     [JsonPropertyName("original")]
    //     public object Original { get; set; }
    // }

    public partial class LineItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("price_id")]
        public string? PriceId { get; set; }

        // [JsonPropertyName("quantity")]
        // public long Quantity { get; set; }

        // [JsonPropertyName("totals")]
        // public Totals Totals { get; set; }

        // [JsonPropertyName("product")]
        // public Product Product { get; set; }

        // [JsonPropertyName("tax_rate")]
        // public string TaxRate { get; set; }

        // [JsonPropertyName("unit_totals")]
        // public Totals UnitTotals { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("proration")]
        public Proration? Proration { get; set; }
    }

    // public partial class Product
    // {
    //     [JsonPropertyName("id")]
    //     public string Id { get; set; }

    //     [JsonPropertyName("name")]
    //     public string Name { get; set; }

    //     [JsonPropertyName("description")]
    //     public string Description { get; set; }

    //     [JsonPropertyName("type")]
    //     public TypeEnum Type { get; set; }

    //     [JsonPropertyName("tax_category")]
    //     public TypeEnum TaxCategory { get; set; }

    //     [JsonPropertyName("image_url")]
    //     public Uri ImageUrl { get; set; }

    //     [JsonPropertyName("custom_data")]
    //     public CustomData CustomData { get; set; }

    //     [JsonPropertyName("status")]
    //     public Status Status { get; set; }

    //     [JsonPropertyName("created_at")]
    //     public DateTimeOffset CreatedAt { get; set; }

    //     [JsonPropertyName("updated_at")]
    //     public DateTimeOffset UpdatedAt { get; set; }

    //     [JsonPropertyName("import_meta")]
    //     public object ImportMeta { get; set; }
    // }

    // public partial class CustomData
    // {
    //     [JsonPropertyName("features")]
    //     public Features Features { get; set; }

    //     [JsonPropertyName("suggested_addons")]
    //     public string[] SuggestedAddons { get; set; }

    //     [JsonPropertyName("upgrade_description")]
    //     public string UpgradeDescription { get; set; }
    // }

    // public partial class Features
    // {
    //     [JsonPropertyName("aircraft_performance")]
    //     public bool AircraftPerformance { get; set; }

    //     [JsonPropertyName("compliance_monitoring")]
    //     public bool ComplianceMonitoring { get; set; }

    //     [JsonPropertyName("flight_log_management")]
    //     public bool FlightLogManagement { get; set; }

    //     [JsonPropertyName("payment_by_invoice")]
    //     public bool PaymentByInvoice { get; set; }

    //     [JsonPropertyName("route_planning")]
    //     public bool RoutePlanning { get; set; }

    //     [JsonPropertyName("sso")]
    //     public bool Sso { get; set; }
    // }

    public partial class Proration
    {
        [JsonPropertyName("billing_period")]
        public BillingPeriod? BillingPeriod { get; set; }
    }

    // public partial class Totals
    // {
    //     [JsonPropertyName("subtotal")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Subtotal { get; set; }

    //     [JsonPropertyName("discount")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Discount { get; set; }

    //     [JsonPropertyName("tax")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Tax { get; set; }

    //     [JsonPropertyName("total")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Total { get; set; }
    // }

    // public partial class TaxRatesUsed
    // {
    //     [JsonPropertyName("tax_rate")]
    //     public string TaxRate { get; set; }

    //     [JsonPropertyName("totals")]
    //     public Totals Totals { get; set; }
    // }

    public partial class Item
    {
        [JsonPropertyName("price")]
        public Price? Price { get; set; }

        [JsonPropertyName("quantity")]
        public long Quantity { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("proration")]
        public Proration? Proration { get; set; }
    }

    public partial class Price
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        // [JsonPropertyName("description")]
        // public string Description { get; set; }

        // [JsonPropertyName("type")]
        // public TypeEnum Type { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        // [JsonPropertyName("product_id")]
        // public string ProductId { get; set; }

        // [JsonPropertyName("billing_cycle")]
        // public PaymentTerms BillingCycle { get; set; }

        // [JsonPropertyName("trial_period")]
        // public object TrialPeriod { get; set; }

        // [JsonPropertyName("tax_mode")]
        // public TaxMode TaxMode { get; set; }

        // [JsonPropertyName("unit_price")]
        // public UnitPrice UnitPrice { get; set; }

        // [JsonPropertyName("unit_price_overrides")]
        // public object[] UnitPriceOverrides { get; set; }

        // [JsonPropertyName("custom_data")]
        // public object CustomData { get; set; }

        // [JsonPropertyName("quantity")]
        // public Quantity Quantity { get; set; }

        // [JsonPropertyName("status")]
        // public Status Status { get; set; }

        // [JsonPropertyName("created_at")]
        // public DateTimeOffset CreatedAt { get; set; }

        // [JsonPropertyName("updated_at")]
        // public DateTimeOffset UpdatedAt { get; set; }

        // [JsonPropertyName("import_meta")]
        // public object ImportMeta { get; set; }
    }

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
    //     public CurrencyCode CurrencyCode { get; set; }
    // }

    // public partial class Payment
    // {
    //     [JsonPropertyName("payment_attempt_id")]
    //     public Guid PaymentAttemptId { get; set; }

    //     [JsonPropertyName("stored_payment_method_id")]
    //     public Guid StoredPaymentMethodId { get; set; }

    //     [JsonPropertyName("payment_method_id")]
    //     public string PaymentMethodId { get; set; }

    //     [JsonPropertyName("amount")]
    //     [JsonConverter(typeof(ParseStringConverter))]
    //     public long Amount { get; set; }

    //     [JsonPropertyName("status")]
    //     public string Status { get; set; }

    //     [JsonPropertyName("error_code")]
    //     public string ErrorCode { get; set; }

    //     [JsonPropertyName("method_details")]
    //     public MethodDetails MethodDetails { get; set; }

    //     [JsonPropertyName("created_at")]
    //     public DateTimeOffset CreatedAt { get; set; }

    //     [JsonPropertyName("captured_at")]
    //     public DateTimeOffset? CapturedAt { get; set; }
    // }

    // public partial class MethodDetails
    // {
    //     [JsonPropertyName("type")]
    //     public string Type { get; set; }

    //     [JsonPropertyName("card")]
    //     public Card Card { get; set; }
    // }

    // public partial class Card
    // {
    //     [JsonPropertyName("type")]
    //     public string Type { get; set; }

    //     [JsonPropertyName("last4")]
    //     public string Last4 { get; set; }

    //     [JsonPropertyName("expiry_month")]
    //     public long ExpiryMonth { get; set; }

    //     [JsonPropertyName("expiry_year")]
    //     public long ExpiryYear { get; set; }

    //     [JsonPropertyName("cardholder_name")]
    //     public string CardholderName { get; set; }
    // }

    public partial class Pagination
    {
        [JsonPropertyName("per_page")]
        public long PerPage { get; set; }

        [JsonPropertyName("next")]
        public Uri? Next { get; set; }

        [JsonPropertyName("has_more")]
        public bool HasMore { get; set; }

        [JsonPropertyName("estimated_total")]
        public long EstimatedTotal { get; set; }
    }
}
