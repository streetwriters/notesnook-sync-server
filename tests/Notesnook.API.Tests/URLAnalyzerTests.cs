using Microsoft.Extensions.Logging.Abstractions;
using Streetwriters.Common.Services;

namespace Notesnook.API.Tests;

public class URLAnalyzerTests
{
    static URLAnalyzerTests()
    {
        Environment.SetEnvironmentVariable("WEBRISK_API_URI", "http://webrisk.invalid/check");
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    [Fact]
    public async Task Unreachable_webrisk_treats_urls_as_safe()
    {
        foreach (var exception in new Exception[] { new HttpRequestException("unreachable"), new TaskCanceledException("timed out") })
        {
            var analyzer = new URLAnalyzer(new HttpClient(new ThrowingHandler(exception)), NullLogger<URLAnalyzer>.Instance);

            Assert.True(await analyzer.IsURLSafeAsync("https://example.com"));
        }
    }
}
