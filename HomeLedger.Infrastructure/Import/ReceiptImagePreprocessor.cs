using SkiaSharp;

namespace HomeLedger.Infrastructure.Import;

public sealed record ReceiptVisionImage(
    byte[] Content,
    string MimeType,
    int Width,
    int Height,
    bool Transformed,
    bool Cropped = false);

public static class ReceiptImagePreprocessor
{
    public const int JpegQuality = 90;

    public static ReceiptVisionImage Prepare(byte[] content, string mimeType, int maxEdgePixels)
    {
        if (content.Length == 0 || maxEdgePixels <= 0)
            return Unchanged(content, mimeType);

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return Unchanged(content, mimeType);

        var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
            return Unchanged(content, mimeType);

        try
        {
            bitmap = ApplyEncodedOrigin(bitmap, codec.EncodedOrigin);
            var cropped = false;
            if (ReceiptRegionDetector.TryFindCrop(bitmap, out var crop))
            {
                bitmap = Crop(bitmap, crop);
                cropped = true;
            }

            bitmap = ResizeToMaxEdge(bitmap, maxEdgePixels);

            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            if (encoded is null || encoded.Size == 0)
                return Unchanged(content, mimeType);

            return new ReceiptVisionImage(
                encoded.ToArray(),
                "image/jpeg",
                bitmap.Width,
                bitmap.Height,
                Transformed: true,
                cropped);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static ReceiptVisionImage Unchanged(byte[] content, string mimeType) =>
        new(content, mimeType, Width: 0, Height: 0, Transformed: false);

    private static SKBitmap Crop(SKBitmap source, SKRectI crop)
    {
        var dest = new SKBitmap(crop.Width, crop.Height);
        using var canvas = new SKCanvas(dest);
        canvas.DrawBitmap(source, crop, new SKRect(0, 0, crop.Width, crop.Height));
        source.Dispose();
        return dest;
    }

    private static SKBitmap ResizeToMaxEdge(SKBitmap source, int maxEdgePixels)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxEdgePixels)
            return source;

        var scale = maxEdgePixels / (double)longEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = source.Resize(
            new SKImageInfo(width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null)
            return source;

        source.Dispose();
        return resized;
    }

    private static SKBitmap ApplyEncodedOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return source;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dest = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(dest);
        canvas.Clear(SKColors.White);
        ApplyOriginTransform(canvas, origin, source.Width, source.Height);
        canvas.DrawBitmap(source, 0, 0);
        source.Dispose();
        return dest;
    }

    private static void ApplyOriginTransform(SKCanvas canvas, SKEncodedOrigin origin, int width, int height)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.Scale(-1, -1);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(0, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(height, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(height, width);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                canvas.Translate(-width, 0);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, width);
                canvas.RotateDegrees(270);
                break;
        }
    }
}
