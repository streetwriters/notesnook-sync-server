namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class GetTransactionInvoiceResponse
    {
        [JsonPropertyName("data")]
        public Invoice Invoice { get; set; }

        [JsonPropertyName("meta")]
        public Meta Meta { get; set; }
    }

    public partial class Invoice
    {

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}
