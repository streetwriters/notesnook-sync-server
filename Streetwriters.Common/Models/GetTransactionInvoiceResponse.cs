namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public class GetTransactionInvoiceResponse : PaddleResponse
    {
        [JsonPropertyName("data")]
        public Invoice Invoice { get; set; }
    }

    public partial class Invoice
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}
