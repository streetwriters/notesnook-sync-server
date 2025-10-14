namespace Streetwriters.Common.Models
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    public partial class PaddleResponse
    {
        [JsonPropertyName("error")]
        public PaddleError? Error { get; set; }
    }

    public class PaddleError
    {
        public string? Type { get; set; }
        public string? Code { get; set; }
        public string? Detail { get; set; }
        [JsonPropertyName("documentation_url")]
        public string? DocumentationUrl { get; set; }
    }
}