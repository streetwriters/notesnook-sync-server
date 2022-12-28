using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Extensions
{
    public static class HttpClientExtensions
    {
        public static async Task<T> SendRequestAsync<T>(this HttpClient httpClient, string url, IHeaderDictionary headers, HttpMethod method, HttpContent content = null) where T : IResponse, new()
        {
            var request = new HttpRequestMessage(method, url);

            if (method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                request.Content = content;
            }

            foreach (var header in headers)
            {
                if (header.Key == "Content-Type" || header.Key == "Content-Length")
                {
                    if (request.Content != null)
                        request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
                    continue;
                }
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
            }

            var response = await httpClient.SendAsync(request);
            if (response.Content.Headers.ContentLength > 0)
            {
                var res = await response.Content.ReadFromJsonAsync<T>();
                res.Success = response.IsSuccessStatusCode;
                res.StatusCode = (int)response.StatusCode;
                return res;
            }
            else
            {
                return new T { Success = response.IsSuccessStatusCode, StatusCode = (int)response.StatusCode };
            }
        }

        public static Task<T> ForwardAsync<T>(this HttpClient httpClient, IHttpContextAccessor accessor, string url, HttpMethod method) where T : IResponse, new()
        {
            var httpContext = accessor.HttpContext;
            var content = new StreamContent(httpContext.Request.BodyReader.AsStream());
            return httpClient.SendRequestAsync<T>(url, httpContext.Request.Headers, method, content);
        }
    }
}