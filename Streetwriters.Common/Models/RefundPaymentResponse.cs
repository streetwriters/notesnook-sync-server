using System;
using System.Text.Json.Serialization;

namespace Streetwriters.Common.Models
{
    public partial class RefundPaymentResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("response")]
        public Refund Refund { get; set; }
    }

    public partial class Refund
    {
        [JsonPropertyName("refund_request_id")]
        public long RefundRequestId { get; set; }
    }
}
