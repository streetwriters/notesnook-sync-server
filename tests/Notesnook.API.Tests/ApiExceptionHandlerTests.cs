using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Notesnook.API.Helpers;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Exceptions;
using WampSharp.V2.Core.Contracts;

namespace Notesnook.API.Tests;

public class ApiExceptionHandlerTests
{
    private static async Task<(int Status, JsonElement Body, HttpResponse Response)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        return (context.Response.StatusCode, body, context.Response);
    }

    [Fact]
    public async Task Client_errors_keep_their_message()
    {
        foreach (var (exception, status) in new (Exception, int)[] { (new ClientException("Storage limit reached."), 400), (new NotFoundException("Storage limit reached."), 404), (new ConflictException("Storage limit reached."), 409) })
        {
            var (actualStatus, body, _) = await HandleAsync(exception);

            Assert.Equal(status, actualStatus);
            Assert.Equal("Storage limit reached.", body.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task Service_unavailable_sets_retry_after()
    {
        var (status, body, response) = await HandleAsync(new S3StorageUnavailableException("Attachment storage is temporarily unavailable. Please try again later."));

        Assert.Equal(503, status);
        Assert.Equal("Attachment storage is temporarily unavailable. Please try again later.", body.GetProperty("error").GetString());
        Assert.Equal("30", response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Unreachable_dependencies_are_503()
    {
        foreach (var exception in new Exception[] { new WampConnectionBrokenException("x"), new TimeoutException("x"), new HttpRequestException("x") })
        {
            var (status, body, response) = await HandleAsync(exception);

            Assert.Equal(503, status);
            Assert.DoesNotContain("x", body.GetProperty("error").GetString()!.Split(' '));
            Assert.False(string.IsNullOrEmpty(response.Headers.RetryAfter));
        }
    }

    [Fact]
    public async Task Other_errors_are_500_without_their_message()
    {
        var (status, body, _) = await HandleAsync(new Exception("Could not delete user data."));

        Assert.Equal(500, status);
        Assert.DoesNotContain("delete", body.GetProperty("error").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }
}
