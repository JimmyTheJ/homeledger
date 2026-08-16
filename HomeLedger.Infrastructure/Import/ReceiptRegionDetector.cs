using SkiaSharp;

namespace HomeLedger.Infrastructure.Import;

public static class ReceiptRegionDetector
{
    internal const int WorkingMaxEdge = 512;
    internal const double MinAreaFraction = 0.10;
    internal const double MaxAreaFraction = 0.90;
    internal const double PaddingFraction = 0.08;

    public static bool TryFindCrop(SKBitmap source, out SKRectI crop)
    {
        crop = SKRectI.Create(0, 0, source.Width, source.Height);
        if (source.Width < 32 || source.Height < 32)
            return false;

        SKBitmap? workingCopy = null;
        var working = source;
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge > WorkingMaxEdge)
        {
            var scale = WorkingMaxEdge / (double)longEdge;
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            workingCopy = source.Resize(
                new SKImageInfo(width, height),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (workingCopy is not null)
                working = workingCopy;
        }

        try
        {
            if (!TryFindWorkingCrop(working, out var workingCrop))
                return false;

            crop = MapRect(workingCrop, working.Width, working.Height, source.Width, source.Height);
            return IsUsefulCrop(crop, source.Width, source.Height);
        }
        finally
        {
            workingCopy?.Dispose();
        }
    }

    private static bool TryFindWorkingCrop(SKBitmap working, out SKRectI crop)
    {
        crop = default;
        var width = working.Width;
        var height = working.Height;
        var pixels = working.Pixels;
        var luma = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
            luma[i] = Luma(pixels[i]);

        var cutoff = Math.Max(Otsu(luma), 100);
        var mask = new bool[luma.Length];
        var brightCount = 0;
        for (var i = 0; i < luma.Length; i++)
        {
            if (luma[i] > cutoff)
            {
                mask[i] = true;
                brightCount++;
            }
        }

        var brightFraction = brightCount / (double)luma.Length;
        if (brightFraction < MinAreaFraction || brightFraction > MaxAreaFraction)
            return false;

        Dilate(mask, width, height);
        Dilate(mask, width, height);

        if (!TryLargestComponent(mask, width, height, out var left, out var top, out var right, out var bottom))
            return false;

        var area = (right - left + 1) * (bottom - top + 1);
        var areaFraction = area / (double)luma.Length;
        if (areaFraction < MinAreaFraction || areaFraction > MaxAreaFraction)
            return false;

        var padX = Math.Max(4, (int)Math.Ceiling((right - left + 1) * PaddingFraction));
        var padY = Math.Max(4, (int)Math.Ceiling((bottom - top + 1) * PaddingFraction));
        left = Math.Max(0, left - padX);
        top = Math.Max(0, top - padY);
        right = Math.Min(width - 1, right + padX);
        bottom = Math.Min(height - 1, bottom + padY);

        crop = SKRectI.Create(left, top, right - left + 1, bottom - top + 1);
        return crop.Width >= 16 && crop.Height >= 16;
    }

    private static bool TryLargestComponent(
        bool[] mask,
        int width,
        int height,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = minY = maxX = maxY = 0;
        var visited = new bool[mask.Length];
        var bestArea = 0;
        var stack = new Stack<int>();

        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || visited[start])
                continue;

            var area = 0;
            var localMinX = width;
            var localMinY = height;
            var localMaxX = 0;
            var localMaxY = 0;
            stack.Push(start);
            visited[start] = true;

            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % width;
                var y = index / width;
                area++;
                if (x < localMinX) localMinX = x;
                if (y < localMinY) localMinY = y;
                if (x > localMaxX) localMaxX = x;
                if (y > localMaxY) localMaxY = y;

                TryPush(index - 1, x > 0);
                TryPush(index + 1, x + 1 < width);
                TryPush(index - width, y > 0);
                TryPush(index + width, y + 1 < height);
            }

            if (area <= bestArea)
                continue;

            bestArea = area;
            minX = localMinX;
            minY = localMinY;
            maxX = localMaxX;
            maxY = localMaxY;

            void TryPush(int neighbor, bool inBounds)
            {
                if (!inBounds || visited[neighbor] || !mask[neighbor])
                    return;
                visited[neighbor] = true;
                stack.Push(neighbor);
            }
        }

        return bestArea > 0;
    }

    private static void Dilate(bool[] mask, int width, int height)
    {
        var next = new bool[mask.Length];
        Array.Copy(mask, next, mask.Length);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (mask[y * width + x])
                    continue;

                var brightNeighbor = false;
                for (var ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1) && !brightNeighbor; ny++)
                {
                    for (var nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                    {
                        if (mask[ny * width + nx])
                        {
                            brightNeighbor = true;
                            break;
                        }
                    }
                }

                if (brightNeighbor)
                    next[y * width + x] = true;
            }
        }

        Array.Copy(next, mask, mask.Length);
    }

    private static SKRectI MapRect(SKRectI workingCrop, int workingWidth, int workingHeight, int sourceWidth, int sourceHeight)
    {
        var left = (int)Math.Floor(workingCrop.Left * sourceWidth / (double)workingWidth);
        var top = (int)Math.Floor(workingCrop.Top * sourceHeight / (double)workingHeight);
        var right = (int)Math.Ceiling(workingCrop.Right * sourceWidth / (double)workingWidth);
        var bottom = (int)Math.Ceiling(workingCrop.Bottom * sourceHeight / (double)workingHeight);
        left = Math.Clamp(left, 0, sourceWidth - 1);
        top = Math.Clamp(top, 0, sourceHeight - 1);
        right = Math.Clamp(right, left + 1, sourceWidth);
        bottom = Math.Clamp(bottom, top + 1, sourceHeight);
        return SKRectI.Create(left, top, right - left, bottom - top);
    }

    private static bool IsUsefulCrop(SKRectI crop, int width, int height)
    {
        var insetX = Math.Max(4, width / 25);
        var insetY = Math.Max(4, height / 25);
        if (crop.Left <= insetX && crop.Top <= insetY && crop.Right >= width - insetX && crop.Bottom >= height - insetY)
            return false;

        return crop.Width >= 16 && crop.Height >= 16
            && crop.Width < width && crop.Height < height;
    }

    private static int Otsu(byte[] luma)
    {
        var hist = new int[256];
        foreach (var value in luma)
            hist[value]++;

        var total = luma.Length;
        long sum = 0;
        for (var i = 0; i < 256; i++)
            sum += i * (long)hist[i];

        long sumB = 0;
        var wB = 0;
        var best = 128;
        double maxVar = 0;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0)
                continue;

            var wF = total - wB;
            if (wF == 0)
                break;

            sumB += t * (long)hist[t];
            var mB = sumB / (double)wB;
            var mF = (sum - sumB) / (double)wF;
            var between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between <= maxVar)
                continue;

            maxVar = between;
            best = t;
        }

        return best;
    }

    private static byte Luma(SKColor color) =>
        (byte)((color.Red * 77 + color.Green * 150 + color.Blue * 29) >> 8);
}
