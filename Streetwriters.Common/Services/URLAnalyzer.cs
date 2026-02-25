using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Services
{
    struct Threat
    {
        public string[]? ThreatTypes { get; set; }
    }
    struct WebRiskAPIResponse
    {
        public Threat Threat { get; set; }
    }

    public class URLAnalyzer : IURLAnalyzer, IDisposable
    {
        private readonly HttpClient httpClient = new();

        public async Task<bool> IsURLSafeAsync(string uri)
        {
            if (string.IsNullOrEmpty(Constants.WEBRISK_API_URI)) return true;
            var body = new StringContent(JsonSerializer.Serialize(new { uri }), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            var response = await httpClient.PostAsync(Constants.WEBRISK_API_URI, body);
            if (!response.IsSuccessStatusCode) return true;
            var json = await response.Content.ReadFromJsonAsync<WebRiskAPIResponse>();
            return json.Threat.ThreatTypes == null || json.Threat.ThreatTypes.Length == 0;
        }

        void IDisposable.Dispose()
        {
            httpClient.Dispose();
        }
    }
}
