using System;
using System.Net.Http;
using System.Net.Http.Json;
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
            var response = await httpClient.PostAsJsonAsync(Constants.WEBRISK_API_URI, new { uri });
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
