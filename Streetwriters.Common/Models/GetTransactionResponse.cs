namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class GetTransactionResponse : PaddleResponse
    {
        [JsonPropertyName("data")]
        public TransactionV2? Transaction { get; set; }
    }
}
