/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
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
        public static async Task<T> SendRequestAsync<T>(this HttpClient httpClient, string url, IHeaderDictionary? headers, HttpMethod method, HttpContent? content = null) where T : IResponse, new()
        {
            var request = new HttpRequestMessage(method, url);

            if (method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                request.Content = content;
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    if (header.Key == "Content-Type" || header.Key == "Content-Length")
                    {
                        request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
                        continue;
                    }
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
                }
            }

            var response = await httpClient.SendAsync(request);
            if (response.Content.Headers.ContentLength > 0 && response.Content.Headers.ContentType?.ToString()?.Contains("application/json") == true)
            {
                var res = await response.Content.ReadFromJsonAsync<T>();
                if (res != null)
                {
                    res.Success = response.IsSuccessStatusCode;
                    res.StatusCode = (int)response.StatusCode;
                    return res;
                }
            }

            return new T { Success = response.IsSuccessStatusCode, StatusCode = (int)response.StatusCode, Content = response.Content };
        }

        public static Task<T> ForwardAsync<T>(this HttpClient httpClient, IHttpContextAccessor accessor, string url, HttpMethod method) where T : IResponse, new()
        {
            var httpContext = accessor.HttpContext ?? throw new InvalidOperationException("HttpContext is not available");
            var content = new StreamContent(httpContext.Request.BodyReader.AsStream());
            return httpClient.SendRequestAsync<T>(url, httpContext.Request.Headers, method, content);
        }
    }
}