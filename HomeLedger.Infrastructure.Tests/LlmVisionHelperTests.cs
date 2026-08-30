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

    [Theory]
    [InlineData("GGML_ASSERT(a->ne[2] * 4 == b->ne[0]) failed", true)]
    [InlineData("signal arrived during cgo execution", true)]
    [InlineData("CUDA error: out of memory", true)]
    [InlineData("Could not reach the vision model: 500", false)]
    public void IsModelRunnerAssert_detects_qwen_crash_text(string message, bool expected)
    {
        Assert.Equal(expected, LlmVisionHelper.IsModelRunnerAssert(new HttpRequestException(message)));
    }

    [Fact]
    public void ResolveOllamaNativeChatUri_strips_openai_v1_prefix()
    {
        var uri = LlmVisionHelper.ResolveOllamaNativeChatUri(new Uri("http://aiweb_ollama:11434/v1/"));
        Assert.Equal("http://aiweb_ollama:11434/api/chat", uri.ToString());
    }

    [Fact]
    public async Task CompleteAsync_logs_and_includes_error_body_on_http_500()
    {
        var logger = new ListLogger();
        var handler = new StubHandler(HttpStatusCode.InternalServerError, """
            {"error":"failed to decode image"}
            """);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://ollama.test/v1/")
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            LlmVisionHelper.CompleteAsync(
                http,
                new LlmSettings
                {
                    BaseUrl = "http://ollama.test/v1",
                    VisionModel = "qwen2.5vl:7b"
                },
                "extract",
                [new StatementPageImage(1, [0xFF, 0xD8, 0xFF], "image/jpeg")],
                logger,
                CancellationToken.None));

        Assert.Contains("500", ex.Message);
        Assert.Contains("failed to decode image", ex.Message);
        var warning = Assert.Single(logger.Messages, m => m.Contains("HTTP 500"));
        Assert.Contains("qwen2.5vl:7b", warning);
        Assert.Contains("failed to decode image", warning);
        Assert.Equal("/api/chat", handler.LastUri?.AbsolutePath);
    }

    [Fact]
    public async Task CompleteAsync_uses_ollama_native_chat_with_num_ctx()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"message":{"role":"assistant","content":"{\"merchant\":\"Farm Boy\"}"}}
            """);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://aiweb_ollama:11434/v1/")
        };

        var text = await LlmVisionHelper.CompleteAsync(
            http,
            new LlmSettings
            {
                BaseUrl = "http://aiweb_ollama:11434/v1",
                VisionModel = "qwen2.5vl:7b",
                NumCtx = 8192,
                VisionMaxTokens = 2048
            },
            "extract",
            [new StatementPageImage(1, [0xFF, 0xD8, 0xFF], "image/jpeg")],
            new ListLogger(),
            CancellationToken.None);

        Assert.Contains("Farm Boy", text);
        Assert.Equal("http://aiweb_ollama:11434/api/chat", handler.LastUri?.ToString());
        Assert.Contains("\"num_ctx\":8192", handler.LastBody);
        Assert.Contains("\"num_predict\":2048", handler.LastBody);
        Assert.Contains("\"images\"", handler.LastBody);
        Assert.DoesNotContain("image_url", handler.LastBody);
    }

    [Fact]
    public async Task CompleteAsync_keeps_openai_chat_completions_without_num_ctx()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"choices":[{"message":{"content":"{}"}}]}
            """);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        await LlmVisionHelper.CompleteAsync(
            http,
            new LlmSettings
            {
                BaseUrl = "https://api.openai.com/v1",
                VisionModel = "gpt-4o"
            },
            "extract",
            [new StatementPageImage(1, [0xFF, 0xD8, 0xFF], "image/jpeg")],
            new ListLogger(),
            CancellationToken.None);

        Assert.Equal("/v1/chat/completions", handler.LastUri?.AbsolutePath);
        Assert.Contains("\"max_tokens\":2048", handler.LastBody);
        Assert.DoesNotContain("num_ctx", handler.LastBody);
        Assert.Contains("image_url", handler.LastBody);
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

        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
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

