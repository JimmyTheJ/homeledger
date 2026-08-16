using HomeLedger.Core.Configuration;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class LlmSettingsTests
{
    [Fact]
    public void Defaults_keep_receipt_images_readable()
    {
        var settings = new LlmSettings();
        Assert.Equal(1536, settings.MaxReceiptImageEdgePixels);
        Assert.False(settings.CropReceiptBackground);
        Assert.Equal(2048, settings.VisionMaxTokens);
        Assert.Equal(0, settings.NumCtx);
        Assert.Equal(2048, settings.ResolvedVisionMaxTokens);
        Assert.Equal(0, settings.ResolvedNumCtx);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(8192, 8192)]
    [InlineData(100, 2048)]
    [InlineData(99999, 32768)]
    public void ResolvedNumCtx_clamps_or_omits(int configured, int expected)
    {
        var settings = new LlmSettings { NumCtx = configured };
        Assert.Equal(expected, settings.ResolvedNumCtx);
    }

    [Theory]
    [InlineData(0, 2048)]
    [InlineData(-1, 2048)]
    [InlineData(2048, 2048)]
    [InlineData(100, 256)]
    [InlineData(99999, 8192)]
    public void ResolvedVisionMaxTokens_clamps_or_defaults(int configured, int expected)
    {
        var settings = new LlmSettings { VisionMaxTokens = configured };
        Assert.Equal(expected, settings.ResolvedVisionMaxTokens);
    }

    [Theory]
    [InlineData("http://aiweb_ollama:11434/v1", true)]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("http://192.168.1.10:11434/v1", true)]
    [InlineData("http://ollama.lan/v1", true)]
    [InlineData("https://api.openai.com/v1", false)]
    [InlineData("http://localhost:1234/v1", false)]
    public void LooksLikeOllama_detects_host_or_default_port(string baseUrl, bool expected)
    {
        var settings = new LlmSettings { BaseUrl = baseUrl };
        Assert.Equal(expected, settings.LooksLikeOllama());
    }
}
