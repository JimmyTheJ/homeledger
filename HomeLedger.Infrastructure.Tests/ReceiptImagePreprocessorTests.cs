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

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/jpeg", maxEdgePixels: 1600, cropBackground: false);

        Assert.True(prepared.Transformed);
        Assert.Equal("image/jpeg", prepared.MimeType);
        Assert.Equal(1596, prepared.Width);
        Assert.Equal(784, prepared.Height);
        Assert.False(prepared.Cropped);
        Assert.True(IsJpeg(prepared.Content));
        Assert.True(prepared.Content.Length < original.Length);
        Assert.Equal(0, prepared.Width % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.Equal(0, prepared.Height % ReceiptImagePreprocessor.VisionPatchMultiple);
    }

    [Fact]
    public void Prepare_reencodes_smaller_images_onto_the_vision_grid()
    {
        var original = EncodeJpeg(800, 600, SKColors.White);

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/jpeg", maxEdgePixels: 1600, cropBackground: false);

        Assert.True(prepared.Transformed);
        Assert.Equal(784, prepared.Width);
        Assert.Equal(588, prepared.Height);
        Assert.True(IsJpeg(prepared.Content));
    }

    [Fact]
    public void Prepare_strips_trailing_motion_photo_bytes()
    {
        var jpeg = EncodeJpeg(800, 600, SKColors.White);
        var withTrailer = new byte[jpeg.Length + 64];
        jpeg.CopyTo(withTrailer, 0);
        Random.Shared.NextBytes(withTrailer.AsSpan(jpeg.Length));

        var prepared = ReceiptImagePreprocessor.Prepare(withTrailer, "image/jpeg", maxEdgePixels: 1600, cropBackground: false);

        Assert.True(prepared.Transformed);
        Assert.True(IsJpeg(prepared.Content));
        Assert.True(prepared.Content.Length < withTrailer.Length);
    }

    [Theory]
    [InlineData(2000, 1000, 1600, 1596, 784)]
    [InlineData(800, 600, 1600, 784, 588)]
    [InlineData(1024, 1024, 1024, 1008, 1008)]
    [InlineData(800, 2800, 1536, 560, 2016)]
    public void TargetSize_caps_long_edge_on_the_28px_grid(
        int width,
        int height,
        int maxEdge,
        int expectedWidth,
        int expectedHeight)
    {
        var size = ReceiptImagePreprocessor.TargetSize(width, height, maxEdge);
        Assert.Equal(expectedWidth, size.Width);
        Assert.Equal(expectedHeight, size.Height);
        Assert.Equal(0, size.Width % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.Equal(0, size.Height % ReceiptImagePreprocessor.VisionPatchMultiple);
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
    public void Prepare_does_not_crop_desk_photos_when_disabled()
    {
        using var bitmap = DeskPhoto(portraitReceipt: true);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var original = encoded.ToArray();

        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/png", maxEdgePixels: 1536, cropBackground: false);

        Assert.False(prepared.Cropped);
        Assert.True(prepared.Transformed);
        Assert.Equal(0, prepared.Width % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.Equal(0, prepared.Height % ReceiptImagePreprocessor.VisionPatchMultiple);
    }

    [Fact]
    public void Prepare_crops_a_portrait_receipt_on_a_dark_desk()
    {
        using var bitmap = DeskPhoto(portraitReceipt: true);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        var prepared = ReceiptImagePreprocessor.Prepare(
            encoded.ToArray(),
            "image/png",
            maxEdgePixels: 1536,
            cropBackground: true);

        Assert.True(prepared.Cropped);
        Assert.True(prepared.Width < 1200);
        Assert.True(prepared.Height > prepared.Width);
        Assert.True(prepared.Width >= ReceiptImagePreprocessor.MinSafeCropShortEdge);
        Assert.Equal(0, prepared.Width % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.Equal(0, prepared.Height % ReceiptImagePreprocessor.VisionPatchMultiple);
    }

    [Fact]
    public void Prepare_rejects_a_crop_that_would_make_line_items_unreadable()
    {
        using var bitmap = new SKBitmap(1600, 1200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        canvas.DrawRect(SKRect.Create(700, 40, 180, 1120), new SKPaint { Color = SKColors.White });
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        var prepared = ReceiptImagePreprocessor.Prepare(
            encoded.ToArray(),
            "image/png",
            maxEdgePixels: 1536,
            cropBackground: true);

        Assert.False(prepared.Cropped);
        Assert.True(prepared.Width >= 1400);
        Assert.True(prepared.Height >= 1000);
    }

    [Fact]
    public void StretchContrastIfFaded_ignores_already_contrasty_images()
    {
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.DrawRect(SKRect.Create(8, 8, 48, 48), new SKPaint { Color = SKColors.Black });

        Assert.False(ReceiptImagePreprocessor.StretchContrastIfFaded(bitmap));
    }

    [Fact]
    public void EstimateSkewDegrees_finds_rotated_receipt_lines()
    {
        using var upright = LinedReceipt(400, 500);
        using var tilted = Rotate(upright, 5);

        var angle = ReceiptImagePreprocessor.EstimateSkewDegrees(tilted);

        Assert.InRange(angle, -8, -2);
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

    private static SKBitmap DeskPhoto(bool portraitReceipt)
    {
        var bitmap = new SKBitmap(1600, 1200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { Color = SKColors.White };
        if (portraitReceipt)
            canvas.DrawRect(SKRect.Create(560, 80, 480, 1040), paint);
        else
            canvas.DrawRect(SKRect.Create(200, 480, 1200, 220), paint);
        return bitmap;
    }

    private static SKBitmap LinedReceipt(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, IsStroke = true };
        for (var y = 40; y < height - 40; y += 18)
            canvas.DrawLine(30, y, width - 30, y, paint);
        return bitmap;
    }

    private static SKBitmap Rotate(SKBitmap source, float degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var width = Math.Max(1, (int)Math.Ceiling(source.Width * cos + source.Height * sin));
        var height = Math.Max(1, (int)Math.Ceiling(source.Width * sin + source.Height * cos));
        var dest = new SKBitmap(width, height);
        using var canvas = new SKCanvas(dest);
        canvas.Clear(SKColors.White);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return dest;
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
