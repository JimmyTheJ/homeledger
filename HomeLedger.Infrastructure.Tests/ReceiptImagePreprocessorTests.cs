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
    public void SplitTallIfNeeded_cuts_portrait_receipts_with_overlap()
    {
        var original = EncodeJpeg(800, 2800, SKColors.White);
        var prepared = ReceiptImagePreprocessor.Prepare(original, "image/jpeg", maxEdgePixels: 1536, cropBackground: false);

        var parts = ReceiptImagePreprocessor.SplitTallIfNeeded(
            prepared,
            ReceiptImagePreprocessor.DefaultSplitMinHeightPixels,
            ReceiptImagePreprocessor.DefaultSplitOverlapPixels);

        Assert.Equal(2, parts.Count);
        Assert.Equal(prepared.Width, parts[0].Width);
        Assert.Equal(prepared.Width, parts[1].Width);
        Assert.True(parts[0].Height < prepared.Height);
        Assert.True(parts[1].Height < prepared.Height);
        Assert.True(parts[0].Height + parts[1].Height > prepared.Height);
        Assert.Equal(0, parts[0].Height % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.Equal(0, parts[1].Height % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.True(IsJpeg(parts[0].Content));
        Assert.True(IsJpeg(parts[1].Content));
    }

    [Fact]
    public void ShouldSplit_ignores_square_images_even_when_tall_enough()
    {
        Assert.False(ReceiptImagePreprocessor.ShouldSplit(1512, 1512, 1400));
        Assert.True(ReceiptImagePreprocessor.ShouldSplit(560, 2016, 1400));
        Assert.False(ReceiptImagePreprocessor.ShouldSplit(560, 1120, 1400));
    }

    [Fact]
    public void SplitBands_overlap_covers_the_midpoint()
    {
        var bands = ReceiptImagePreprocessor.SplitBands(2016, 224);

        Assert.Equal(0, bands.TopY);
        Assert.True(bands.TopHeight > 1008);
        Assert.True(bands.BottomY < 1008);
        Assert.Equal(2016, bands.BottomY + bands.BottomHeight);
        Assert.True(bands.TopHeight + bands.BottomHeight > 2016);
        Assert.Equal(0, bands.TopHeight % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.Equal(0, bands.BottomY % ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.True(bands.Extra >= ReceiptImagePreprocessor.VisionPatchMultiple);
        Assert.Equal(bands.TopHeight - bands.Extra, bands.BottomY + bands.Extra);
    }

    [Fact]
    public void PaintContextBand_fades_the_unowned_edge_and_draws_a_cut_line()
    {
        using var bitmap = new SKBitmap(80, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(40, 40, 40));

        ReceiptImagePreprocessor.PaintContextBand(bitmap, fadeFromTop: false, contextHeight: 60);

        var owned = Luma(bitmap.GetPixel(40, 80));
        var faded = Luma(bitmap.GetPixel(40, 280));
        Assert.True(faded > owned + 40);

        var cut = bitmap.GetPixel(40, 240);
        Assert.True(cut.Red > cut.Green + 80);
        Assert.True(cut.Red > cut.Blue + 80);
    }

    [Fact]
    public void SplitTallIfNeeded_marks_context_on_each_tile()
    {
        using var source = LinedReceipt(560, 2016);
        using var encoded = source.Encode(SKEncodedImageFormat.Jpeg, 92);
        var prepared = ReceiptImagePreprocessor.Prepare(
            encoded!.ToArray(),
            "image/jpeg",
            maxEdgePixels: 1536,
            cropBackground: false);

        var parts = ReceiptImagePreprocessor.SplitTallIfNeeded(
            prepared,
            ReceiptImagePreprocessor.DefaultSplitMinHeightPixels,
            ReceiptImagePreprocessor.DefaultSplitOverlapPixels);
        var bands = ReceiptImagePreprocessor.SplitBands(prepared.Height, ReceiptImagePreprocessor.DefaultSplitOverlapPixels);

        Assert.Equal(2, parts.Count);
        AssertHasCutLine(parts[0], bands.Extra, fromTop: false);
        AssertHasCutLine(parts[1], bands.Extra, fromTop: true);
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
    [InlineData(800, 2800, 672, 168, 672)]
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
    public void TargetSize_honors_configured_tall_receipt_cap()
    {
        var scale = new ReceiptVisionScaleOptions(
            FallbackMaxEdgePixels: 672,
            MaxTallReceiptEdgePixels: 1344,
            MinReadableShortEdgePixels: 616,
            MaxVisionPatches: 2304);

        var size = ReceiptImagePreprocessor.TargetSize(800, 2800, 1536, scale);

        Assert.Equal(1344, size.Height);
        Assert.True(size.Width < 560);
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
    [InlineData(1024, 1120)]
    [InlineData(100, 896)]
    [InlineData(99999, 2240)]
    public void ResolvedMaxReceiptImageEdgePixels_snaps_or_disables(int configured, int expected)
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

    private static void AssertHasCutLine(ReceiptVisionImage tile, int extra, bool fromTop)
    {
        using var data = SKData.CreateCopy(tile.Content);
        using var bitmap = SKBitmap.Decode(data);
        Assert.NotNull(bitmap);
        var band = Math.Min(extra, bitmap.Height / 3);
        var cutY = fromTop ? band : bitmap.Height - band;
        var found = false;
        for (var y = Math.Max(0, cutY - 6); y <= Math.Min(bitmap.Height - 1, cutY + 6); y++)
        {
            var pixel = bitmap.GetPixel(bitmap.Width / 2, y);
            if (pixel.Red > pixel.Green + 30 && pixel.Red > pixel.Blue + 30)
                found = true;
        }

        Assert.True(found, $"Expected a red cut line near y={cutY} on the {(fromTop ? "bottom" : "top")} tile.");
    }

    private static int Luma(SKColor color) =>
        (color.Red * 77 + color.Green * 150 + color.Blue * 29) >> 8;
}
