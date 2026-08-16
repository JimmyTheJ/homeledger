using System.Net;
using System.Text;
using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class LlmVisionHelperTests
{
    [Fact]
    public void PreviewResponseBody_collapses_whitespace_and_truncates()
    {
        Assert.Equal("<empty>", LlmVisionHelper.PreviewResponseBody("  \n"));
        Assert.Equal("{ \"error\": \"boom\" }", LlmVisionHelper.PreviewResponseBody("{\n  \"error\": \"boom\"\n}"));
        Assert.Equal("abc…", LlmVisionHelper.PreviewResponseBody("abcdef", maxChars: 3));
    }

    [Fact]
    public async Task CompleteAsync_logs_and_includes_error_body_on_http_500()
    {
        var logger = new ListLogger();
        using var http = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, """
            {"error":"failed to decode image"}
            """))
        {
            BaseAddress = new Uri("http://ollama.test/v1/")
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            LlmVisionHelper.CompleteAsync(
                http,
                new LlmSettings { VisionModel = "qwen2.5vl:7b" },
                "extract",
                [new StatementPageImage(1, [0xFF, 0xD8, 0xFF], "image/jpeg")],
                logger,
                CancellationToken.None));

        Assert.Contains("500", ex.Message);
        Assert.Contains("failed to decode image", ex.Message);
        var warning = Assert.Single(logger.Messages);
        Assert.Contains("qwen2.5vl:7b", warning);
        Assert.Contains("failed to decode image", warning);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
