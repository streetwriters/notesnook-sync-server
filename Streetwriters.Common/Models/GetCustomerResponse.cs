namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class GetCustomerResponse : PaddleResponse
    {
        [JsonPropertyName("data")]
        public PaddleCustomer Customer { get; set; }
    }

    public class PaddleCustomer
    {
        [JsonPropertyName("email")]
        public string Email { get; set; }
    }
}
