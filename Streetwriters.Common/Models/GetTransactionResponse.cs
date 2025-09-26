namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class GetTransactionResponse
    {
        [JsonPropertyName("data")]
        public TransactionV2 Transaction { get; set; }

        [JsonPropertyName("meta")]
        public Meta Meta { get; set; }
    }
}
