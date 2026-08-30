using SkiaSharp;

namespace HomeLedger.Infrastructure.Import;

public sealed record ReceiptVisionImage(
    byte[] Content,
    string MimeType,
    int Width,
    int Height,
    bool Transformed,
    bool Cropped = false,
    bool Deskewed = false,
    bool ContrastEnhanced = false);

public static class ReceiptImagePreprocessor
{
    public const int JpegQuality = 92;
    public const int VisionPatchMultiple = 28;
    public const int FallbackMaxEdgePixels = 672;
    internal const int MinReadableShortEdgePixels = 616;
    internal const int MaxTallReceiptEdgePixels = 2016;
    internal const int MaxVisionPatches = 2304;
    internal const int DefaultSplitMinHeightPixels = 1400;
    internal const int DefaultSplitOverlapPixels = 224;
    internal const int MinSafeCropShortEdge = 280;
    internal const double MaxSafeCropAspect = 6.0;

    public static ReceiptVisionImage Prepare(
        byte[] content,
        string mimeType,
        int maxEdgePixels,
        bool cropBackground = true)
    {
        if (content.Length == 0 || maxEdgePixels <= 0)
            return Unchanged(content, mimeType);

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return Unchanged(content, mimeType);

        var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
            return Unchanged(content, mimeType);

        try
        {
            var oriented = ApplyEncodedOrigin(decoded, codec.EncodedOrigin);
            var ownsOriented = !ReferenceEquals(oriented, decoded);
            try
            {
                var prepared = PrepareFromBitmap(oriented, maxEdgePixels, cropBackground);
                return prepared.Content.Length == 0 ? Unchanged(content, mimeType) : prepared;
            }
            finally
            {
                if (ownsOriented)
                    oriented.Dispose();
            }
        }
        finally
        {
            decoded.Dispose();
        }
    }

    public static ReceiptVisionImage PrepareFromBitmap(
        SKBitmap source,
        int maxEdgePixels,
        bool cropBackground = true)
    {
        if (source.Width <= 0 || source.Height <= 0 || maxEdgePixels <= 0)
            return Unchanged([], "image/jpeg");

        var current = source;
        var owns = false;
        var cropped = false;
        var deskewed = false;
        var contrastEnhanced = false;

        try
        {
            if (cropBackground
                && ReceiptRegionDetector.TryFindCrop(current, out var crop)
                && IsSafeCrop(crop, current.Width, current.Height, maxEdgePixels))
            {
                current = CropCopy(current, crop);
                owns = true;
                cropped = true;
            }

            if (TryDeskew(current, out var straightened))
            {
                if (owns)
                    current.Dispose();
                current = straightened;
                owns = true;
                deskewed = true;
            }

            if (!owns)
            {
                var copy = current.Copy();
                if (copy is not null)
                {
                    current = copy;
                    owns = true;
                }
            }

            contrastEnhanced = StretchContrastIfFaded(current);

            var (width, height) = TargetSize(current.Width, current.Height, maxEdgePixels);
            if (width != current.Width || height != current.Height)
            {
                var resized = current.Resize(
                    new SKImageInfo(width, height),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized is not null)
                {
                    if (owns)
                        current.Dispose();
                    current = resized;
                    owns = true;
                }
            }

            using var encoded = current.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            if (encoded is null || encoded.Size == 0)
                return Unchanged([], "image/jpeg");

            return new ReceiptVisionImage(
                encoded.ToArray(),
                "image/jpeg",
                current.Width,
                current.Height,
                Transformed: true,
                cropped,
                deskewed,
                contrastEnhanced);
        }
        finally
        {
            if (owns)
                current.Dispose();
        }
    }

    private static ReceiptVisionImage Unchanged(byte[] content, string mimeType) =>
        new(content, mimeType, Width: 0, Height: 0, Transformed: false);

    internal static bool IsSafeCrop(SKRectI crop, int sourceWidth, int sourceHeight, int maxEdgePixels)
    {
        if (crop.Width < 32 || crop.Height < 32)
            return false;

        var aspect = Math.Max(crop.Width, crop.Height) / (double)Math.Max(1, Math.Min(crop.Width, crop.Height));
        if (aspect > MaxSafeCropAspect)
            return false;

        var (width, height) = TargetSize(crop.Width, crop.Height, maxEdgePixels);
        if (Math.Min(width, height) < MinSafeCropShortEdge)
            return false;

        var removed = 1.0 - crop.Width * crop.Height / (double)(sourceWidth * sourceHeight);
        return removed >= 0.08 && removed <= 0.90;
    }

    private static ReceiptVisionImage CropBand(
        SKBitmap source,
        int y,
        int height,
        ReceiptVisionImage prepared)
    {
        var crop = SKRectI.Create(0, y, source.Width, height);
        using var dest = CropCopy(source, crop);
        using var encoded = dest.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        if (encoded is null || encoded.Size == 0)
            return prepared;

        return new ReceiptVisionImage(
            encoded.ToArray(),
            "image/jpeg",
            dest.Width,
            dest.Height,
            Transformed: true,
            prepared.Cropped,
            prepared.Deskewed,
            prepared.ContrastEnhanced);
    }

    private static SKBitmap CropCopy(SKBitmap source, SKRectI crop)
    {
        var dest = new SKBitmap(crop.Width, crop.Height);
        using var canvas = new SKCanvas(dest);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, crop, new SKRect(0, 0, crop.Width, crop.Height));
        return dest;
    }

    internal static (int Width, int Height) TargetSize(int sourceWidth, int sourceHeight, int maxEdgePixels)
    {
        var scale = ComputeScale(sourceWidth, sourceHeight, maxEdgePixels);
        var width = Math.Max(
            VisionPatchMultiple,
            AlignDown((int)Math.Round(sourceWidth * scale), VisionPatchMultiple));
        var height = Math.Max(
            VisionPatchMultiple,
            AlignDown((int)Math.Round(sourceHeight * scale), VisionPatchMultiple));
        return (width, height);
    }

    public static IReadOnlyList<ReceiptVisionImage> SplitTallIfNeeded(
        ReceiptVisionImage prepared,
        int minHeightPixels,
        int overlapPixels)
    {
        if (!ShouldSplit(prepared.Width, prepared.Height, minHeightPixels))
            return [prepared];

        using var data = SKData.CreateCopy(prepared.Content);
        using var bitmap = SKBitmap.Decode(data);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return [prepared];

        var (topY, topHeight, bottomY, bottomHeight) = SplitBands(bitmap.Height, overlapPixels);
        if (topHeight >= bitmap.Height || bottomY <= 0 || bottomHeight <= 0)
            return [prepared];

        return
        [
            CropBand(bitmap, topY, topHeight, prepared),
            CropBand(bitmap, bottomY, bottomHeight, prepared)
        ];
    }

    internal static bool ShouldSplit(int width, int height, int minHeightPixels)
    {
        if (width <= 0 || height < Math.Max(minHeightPixels, VisionPatchMultiple * 16))
            return false;

        return height * 2 >= width * 3;
    }

    internal static (int TopY, int TopHeight, int BottomY, int BottomHeight) SplitBands(
        int height,
        int overlapPixels)
    {
        var mid = AlignDown(height / 2, VisionPatchMultiple);
        var overlap = AlignDown(
            Math.Clamp(overlapPixels, VisionPatchMultiple, Math.Max(VisionPatchMultiple, height / 3)),
            VisionPatchMultiple);
        var extra = AlignDown(Math.Max(VisionPatchMultiple, overlap / 2), VisionPatchMultiple);
        var topHeight = Math.Min(height, mid + extra);
        var bottomY = Math.Max(0, AlignDown(mid - extra, VisionPatchMultiple));
        var bottomHeight = height - bottomY;
        topHeight = AlignDown(topHeight, VisionPatchMultiple);
        return (0, topHeight, bottomY, bottomHeight);
    }

    internal static double ComputeScale(int sourceWidth, int sourceHeight, int maxEdgePixels)
    {
        var longEdge = Math.Max(sourceWidth, sourceHeight);
        var shortEdge = Math.Min(sourceWidth, sourceHeight);
        var maxAligned = AlignDown(Math.Max(maxEdgePixels, VisionPatchMultiple), VisionPatchMultiple);
        var tallAligned = AlignDown(MaxTallReceiptEdgePixels, VisionPatchMultiple);

        var scale = longEdge > maxAligned ? maxAligned / (double)longEdge : 1.0;
        // Crash retries pass FallbackMaxEdgePixels and must actually shrink. The readable-short-edge
        // boost would otherwise keep a 2016-tall receipt at the size that just failed.
        if (maxEdgePixels > FallbackMaxEdgePixels
            && shortEdge * scale < MinReadableShortEdgePixels
            && shortEdge > 0)
        {
            var readableScale = MinReadableShortEdgePixels / (double)shortEdge;
            var tallCapScale = tallAligned / (double)longEdge;
            scale = Math.Min(1.0, Math.Min(readableScale, tallCapScale));
        }

        var patchScale = VisionPatchMultiple * Math.Sqrt(MaxVisionPatches / (double)Math.Max(1L, (long)sourceWidth * sourceHeight));
        scale = Math.Min(scale, patchScale);
        return Math.Clamp(scale, 0.01, 1.0);
    }

    internal static int AlignDown(int value, int factor)
    {
        if (factor <= 1 || value < factor)
            return Math.Max(value, 1);

        return (value / factor) * factor;
    }

    internal static bool StretchContrastIfFaded(SKBitmap bitmap)
    {
        if (bitmap.Width < 8 || bitmap.Height < 8)
            return false;

        var pixels = bitmap.Pixels;
        if (pixels.Length == 0)
            return false;

        var hist = new int[256];
        var luma = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            luma[i] = Luma(pixels[i]);
            hist[luma[i]]++;
        }

        var pLow = Percentile(hist, pixels.Length, 0.02);
        var pHigh = Percentile(hist, pixels.Length, 0.98);
        if (pHigh - pLow < 12 || pHigh - pLow >= 150)
            return false;

        var span = Math.Max(1, pHigh - pLow);
        const int destLow = 18;
        const int destHigh = 238;
        var destSpan = destHigh - destLow;
        var changed = false;

        for (var i = 0; i < pixels.Length; i++)
        {
            var y = luma[i];
            var stretched = destLow + (y - pLow) * destSpan / span;
            stretched = Math.Clamp(stretched, 0, 255);
            if (stretched == y)
                continue;

            var pixel = pixels[i];
            if (y == 0)
            {
                var gray = (byte)stretched;
                pixels[i] = new SKColor(gray, gray, gray, pixel.Alpha);
            }
            else
            {
                var scale = stretched / (double)y;
                pixels[i] = new SKColor(
                    (byte)Math.Clamp((int)Math.Round(pixel.Red * scale), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(pixel.Green * scale), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(pixel.Blue * scale), 0, 255),
                    pixel.Alpha);
            }

            changed = true;
        }

        if (changed)
            bitmap.Pixels = pixels;

        return changed;
    }

    internal static bool TryDeskew(SKBitmap source, out SKBitmap deskewed)
    {
        deskewed = null!;
        var angle = EstimateSkewDegrees(source);
        if (Math.Abs(angle) < 0.75f || Math.Abs(angle) > 12f)
            return false;

        deskewed = RotateWhite(source, angle);
        return true;
    }

    internal static float EstimateSkewDegrees(SKBitmap source)
    {
        if (source.Width < 32 || source.Height < 32)
            return 0;

        using var working = Downscale(source, 256);
        var zeroScore = RowInkVariance(working);
        var bestScore = zeroScore;
        var bestAngle = 0f;

        for (var angle = -8f; angle <= 8.01f; angle += 1f)
        {
            if (Math.Abs(angle) < 0.01f)
                continue;

            using var rotated = RotateWhite(working, angle);
            var score = RowInkVariance(rotated);
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestAngle = angle;
        }

        if (bestScore < 4)
            return 0;
        if (Math.Abs(bestAngle) < 0.5f)
            return 0;
        if (zeroScore > 0 && bestScore < zeroScore * 1.12)
            return 0;

        return bestAngle;
    }

    private static double RowInkVariance(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = bitmap.Pixels;
        var sums = new double[height];
        for (var y = 0; y < height; y++)
        {
            var row = 0;
            var offset = y * width;
            for (var x = 0; x < width; x++)
            {
                if (Luma(pixels[offset + x]) < 150)
                    row++;
            }

            sums[y] = row;
        }

        var mean = sums.Average();
        return sums.Sum(value => (value - mean) * (value - mean)) / height;
    }

    private static SKBitmap Downscale(SKBitmap source, int maxEdge)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxEdge)
            return source.Copy() ?? new SKBitmap(source.Info);

        var scale = maxEdge / (double)longEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(
            new SKImageInfo(width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? source.Copy()
            ?? new SKBitmap(source.Info);
    }

    private static SKBitmap RotateWhite(SKBitmap source, float degrees)
    {
        if (Math.Abs(degrees) < 0.01f)
            return source.Copy() ?? new SKBitmap(source.Info);

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

    private static int Percentile(int[] hist, int total, double fraction)
    {
        var target = Math.Clamp((int)Math.Round(total * fraction), 1, total);
        var cumulative = 0;
        for (var i = 0; i < hist.Length; i++)
        {
            cumulative += hist[i];
            if (cumulative >= target)
                return i;
        }

        return 255;
    }

    private static byte Luma(SKColor color) =>
        (byte)((color.Red * 77 + color.Green * 150 + color.Blue * 29) >> 8);
}
