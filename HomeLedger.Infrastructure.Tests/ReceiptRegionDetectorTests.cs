using HomeLedger.Infrastructure.Import;
using SkiaSharp;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class ReceiptRegionDetectorTests
{
    [Fact]
    public void TryFindCrop_finds_white_receipt_on_dark_background()
    {
        using var bitmap = new SKBitmap(400, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        canvas.DrawRect(SKRect.Create(50, 90, 300, 70), new SKPaint { Color = SKColors.White });

        Assert.True(ReceiptRegionDetector.TryFindCrop(bitmap, out var crop));
        Assert.InRange(crop.Left, 10, 55);
        Assert.InRange(crop.Top, 50, 95);
        Assert.InRange(crop.Right, 340, 390);
        Assert.InRange(crop.Bottom, 150, 195);
    }

    [Fact]
    public void TryFindCrop_keeps_the_larger_bright_region()
    {
        using var bitmap = new SKBitmap(400, 400);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(20, 20, 20));
        using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(SKRect.Create(40, 160, 280, 90), paint);
        canvas.DrawRect(SKRect.Create(330, 20, 40, 40), paint);

        Assert.True(ReceiptRegionDetector.TryFindCrop(bitmap, out var crop));
        Assert.True(crop.Left < 80);
        Assert.True(crop.Right > 280);
        Assert.True(crop.Right < 370);
        Assert.True(crop.Top > 100);
        Assert.True(crop.Bottom < 280);
    }

    [Fact]
    public void TryFindCrop_finds_printed_text_on_a_white_table()
    {
        using var bitmap = new SKBitmap(400, 500);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(245, 245, 245));
        using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2, IsStroke = true };
        for (var y = 80; y < 420; y += 16)
            canvas.DrawLine(70, y, 330, y, paint);

        Assert.True(ReceiptRegionDetector.TryFindCrop(bitmap, out var crop));
        Assert.True(crop.Left >= 20);
        Assert.True(crop.Top >= 20);
        Assert.True(crop.Right <= 380);
        Assert.True(crop.Bottom <= 480);
        Assert.True(crop.Width < 400 || crop.Height < 500);
    }

    [Fact]
    public void TryFindCrop_ignores_full_frame_white()
    {
        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        Assert.False(ReceiptRegionDetector.TryFindCrop(bitmap, out _));
    }

    [Fact]
    public void TryFindCrop_ignores_full_frame_black()
    {
        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        Assert.False(ReceiptRegionDetector.TryFindCrop(bitmap, out _));
    }

    [Fact]
    public void Prepare_crops_then_downscales_a_desk_photo()
    {
        using var bitmap = new SKBitmap(1600, 1200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        canvas.DrawRect(SKRect.Create(560, 80, 480, 1040), new SKPaint { Color = SKColors.White });
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        var prepared = ReceiptImagePreprocessor.Prepare(
            encoded.ToArray(),
            "image/png",
            maxEdgePixels: 1536,
            cropBackground: true);

        Assert.True(prepared.Cropped);
        Assert.True(prepared.Width < 1200);
        Assert.True(prepared.Height > 700);
        Assert.True(prepared.Width > 350);
    }
}
