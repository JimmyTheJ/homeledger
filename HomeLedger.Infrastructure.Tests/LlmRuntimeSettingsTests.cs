using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Llm;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class LlmRuntimeSettingsTests
{
    [Fact]
    public void From_copies_effective_vision_model()
    {
        var settings = new LlmSettings { VisionModel = "qwen3-vl:4b", NumCtx = 8192 };
        var runtime = LlmRuntimeSettings.From(settings);

        Assert.Equal("qwen3-vl:4b", runtime.VisionModel);
        Assert.Equal(8192, runtime.NumCtx);
        Assert.False(runtime.CropReceiptBackground);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(100, 640)]
    [InlineData(1536, 1536)]
    [InlineData(99999, 4096)]
    public void Normalize_clamps_image_edge(int configured, int expected)
    {
        var runtime = new LlmRuntimeSettings { MaxReceiptImageEdgePixels = configured };
        runtime.Normalize();
        Assert.Equal(expected, runtime.MaxReceiptImageEdgePixels);
    }

    [Fact]
    public void Normalize_trims_and_clears_blank_vision_model()
    {
        var runtime = new LlmRuntimeSettings { VisionModel = "  qwen3-vl:4b  " };
        runtime.Normalize();
        Assert.Equal("qwen3-vl:4b", runtime.VisionModel);

        runtime.VisionModel = "   ";
        runtime.Normalize();
        Assert.Null(runtime.VisionModel);
    }
}

public class LlmSettingsOverlayStoreTests
{
    [Fact]
    public async Task SaveAsync_writes_llm_section_and_omits_blank_vision_model()
    {
        var path = Path.Combine(Path.GetTempPath(), "homeledger-overlay-" + Guid.NewGuid().ToString("N"), "llm-settings.json");
        var store = new LlmSettingsOverlayStore(path);

        await store.SaveAsync(new LlmRuntimeSettings
        {
            VisionModel = "  ",
            MaxPdfPages = 30,
            MaxReceiptImages = 20,
            MaxReceiptImageEdgePixels = 1536,
            CropReceiptBackground = true,
            NumCtx = 8192,
            VisionMaxTokens = 2048
        });

        Assert.True(store.Exists);
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("VisionModel", json, StringComparison.Ordinal);
        Assert.Contains("\"CropReceiptBackground\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"NumCtx\": 8192", json, StringComparison.Ordinal);

        await store.ClearAsync();
        Assert.False(store.Exists);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SaveAsync_persists_vision_model()
    {
        var path = Path.Combine(Path.GetTempPath(), "homeledger-overlay-" + Guid.NewGuid().ToString("N"), "llm-settings.json");
        var store = new LlmSettingsOverlayStore(path);

        await store.SaveAsync(new LlmRuntimeSettings { VisionModel = "qwen3-vl:4b" });

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"VisionModel\": \"qwen3-vl:4b\"", json, StringComparison.Ordinal);
    }
}
