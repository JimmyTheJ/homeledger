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
        Assert.True(settings.CropReceiptBackground);
        Assert.True(settings.SplitTallReceipts);
        Assert.Equal(1400, settings.ReceiptSplitMinHeightPixels);
        Assert.Equal(224, settings.ReceiptSplitOverlapPixels);
        Assert.Equal(672, settings.FallbackMaxEdgePixels);
        Assert.Equal(2016, settings.MaxTallReceiptEdgePixels);
        Assert.Equal(616, settings.MinReadableShortEdgePixels);
        Assert.Equal(2304, settings.MaxVisionPatches);
        Assert.Equal(2048, settings.VisionMaxTokens);
        Assert.Equal(0, settings.NumCtx);
        Assert.Equal(2048, settings.ResolvedVisionMaxTokens);
        Assert.Equal(0, settings.ResolvedNumCtx);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(8192, 8192)]
    [InlineData(100, 4096)]
    [InlineData(2048, 4096)]
    [InlineData(99999, 16384)]
    public void ResolvedNumCtx_snaps_to_allowed_values(int configured, int expected)
    {
        var settings = new LlmSettings { NumCtx = configured };
        Assert.Equal(expected, settings.ResolvedNumCtx);
    }

    [Theory]
    [InlineData(0, 1400)]
    [InlineData(1400, 1400)]
    [InlineData(100, 1120)]
    [InlineData(99999, 2016)]
    public void ResolvedReceiptSplitMinHeightPixels_snaps_to_allowed_values(int configured, int expected)
    {
        var settings = new LlmSettings { ReceiptSplitMinHeightPixels = configured };
        Assert.Equal(expected, settings.ResolvedReceiptSplitMinHeightPixels);
    }

    [Theory]
    [InlineData(0, 2048)]
    [InlineData(-1, 2048)]
    [InlineData(2048, 2048)]
    [InlineData(100, 1024)]
    [InlineData(99999, 8192)]
    public void ResolvedVisionMaxTokens_snaps_to_allowed_values(int configured, int expected)
    {
        var settings = new LlmSettings { VisionMaxTokens = configured };
        Assert.Equal(expected, settings.ResolvedVisionMaxTokens);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1536, 1536)]
    [InlineData(1024, 1120)]
    [InlineData(100, 896)]
    [InlineData(99999, 2240)]
    public void ResolvedMaxReceiptImageEdgePixels_snaps_or_disables(int configured, int expected)
    {
        var settings = new LlmSettings { MaxReceiptImageEdgePixels = configured };
        Assert.Equal(expected, settings.ResolvedMaxReceiptImageEdgePixels);
    }

    [Fact]
    public void Fallback_is_forced_below_max_edge()
    {
        var settings = new LlmSettings
        {
            MaxReceiptImageEdgePixels = 896,
            FallbackMaxEdgePixels = 896
        };
        Assert.Equal(672, settings.ResolvedFallbackMaxEdgePixels);
    }

    [Fact]
    public void Tall_edge_is_forced_at_least_max_edge()
    {
        var settings = new LlmSettings
        {
            MaxReceiptImageEdgePixels = 2016,
            MaxTallReceiptEdgePixels = 1344
        };
        Assert.Equal(2016, settings.ResolvedMaxTallReceiptEdgePixels);
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
