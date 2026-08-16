using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Import;
using SkiaSharp;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class ReceiptImagePreprocessorTests
{
    [Fact]
    public void Prepare_downscales_long_edge_and_reencodes_jpeg()
    {
        var original = EncodeJpeg(2000, 1000, SKColors.White);

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/jpeg", maxEdgePixels: 1600);

        Assert.True(prepared.Transformed);
        Assert.Equal("image/jpeg", prepared.MimeType);
        Assert.Equal(1600, prepared.Width);
        Assert.Equal(800, prepared.Height);
        Assert.False(prepared.Cropped);
        Assert.True(IsJpeg(prepared.Content));
        Assert.True(prepared.Content.Length < original.Length);
    }

    [Fact]
    public void Prepare_does_not_upscale_or_reencode_smaller_images()
    {
        var original = EncodeJpeg(800, 600, SKColors.White);

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/jpeg", maxEdgePixels: 1600);

        Assert.False(prepared.Transformed);
        Assert.Same(original, prepared.Content);
    }

    [Theory]
    [InlineData(2000, 1000, 1600, 1600, 800)]
    [InlineData(800, 600, 1600, 800, 600)]
    [InlineData(1024, 1024, 1024, 1024, 1024)]
    [InlineData(800, 2800, 1536, 585, 2048)]
    public void TargetSize_caps_long_edge_but_keeps_tall_receipts_readable(
        int width,
        int height,
        int maxEdge,
        int expectedWidth,
        int expectedHeight)
    {
        var size = ReceiptImagePreprocessor.TargetSize(width, height, maxEdge);
        Assert.Equal(expectedWidth, size.Width);
        Assert.Equal(expectedHeight, size.Height);
    }

    [Fact]
    public void Prepare_returns_original_when_bytes_are_not_an_image()
    {
        var original = "not-an-image"u8.ToArray();

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/jpeg", maxEdgePixels: 1600);

        Assert.False(prepared.Transformed);
        Assert.Same(original, prepared.Content);
    }

    [Fact]
    public void Prepare_skips_work_when_max_edge_is_disabled()
    {
        var original = EncodeJpeg(2000, 1000, SKColors.White);

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/jpeg", maxEdgePixels: 0);

        Assert.False(prepared.Transformed);
        Assert.Same(original, prepared.Content);
    }

    [Fact]
    public void Prepare_does_not_crop_desk_photos_unless_asked()
    {
        using var bitmap = new SKBitmap(1600, 1200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        canvas.DrawRect(SKRect.Create(200, 480, 1200, 220), new SKPaint { Color = SKColors.White });
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var original = encoded.ToArray();

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/png", maxEdgePixels: 1536);

        Assert.False(prepared.Cropped);
        Assert.True(prepared.Transformed);
        Assert.Equal(1536, prepared.Width);
        Assert.Equal(1152, prepared.Height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1536, 1536)]
    [InlineData(1024, 1024)]
    [InlineData(100, 640)]
    [InlineData(99999, 4096)]
    public void ResolvedMaxReceiptImageEdgePixels_clamps_or_disables(int configured, int expected)
    {
        var settings = new LlmSettings { MaxReceiptImageEdgePixels = configured };
        Assert.Equal(expected, settings.ResolvedMaxReceiptImageEdgePixels);
    }

    private static byte[] EncodeJpeg(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    private static bool IsJpeg(byte[] content) =>
        content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
}
