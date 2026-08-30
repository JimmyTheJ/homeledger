namespace HomeLedger.Core.Configuration;

public sealed record LlmChoice(int Value, string Label);

public static class LlmSettingChoices
{
    public static IReadOnlyList<LlmChoice> MaxReceiptImageEdgePixels { get; } =
    [
        new(0, "Disabled (send original)"),
        new(896, "896 px"),
        new(1120, "1,120 px"),
        new(1344, "1,344 px"),
        new(1536, "1,536 px"),
        new(1792, "1,792 px"),
        new(2016, "2,016 px"),
        new(2240, "2,240 px")
    ];

    public static IReadOnlyList<LlmChoice> FallbackMaxEdgePixels { get; } =
    [
        new(448, "448 px"),
        new(560, "560 px"),
        new(672, "672 px"),
        new(896, "896 px")
    ];

    public static IReadOnlyList<LlmChoice> MaxTallReceiptEdgePixels { get; } =
    [
        new(1344, "1,344 px"),
        new(1536, "1,536 px"),
        new(1792, "1,792 px"),
        new(2016, "2,016 px"),
        new(2240, "2,240 px")
    ];

    public static IReadOnlyList<LlmChoice> MinReadableShortEdgePixels { get; } =
    [
        new(448, "448 px"),
        new(560, "560 px"),
        new(616, "616 px"),
        new(672, "672 px"),
        new(784, "784 px")
    ];

    public static IReadOnlyList<LlmChoice> MaxVisionPatches { get; } =
    [
        new(1024, "1,024"),
        new(1536, "1,536"),
        new(2048, "2,048"),
        new(2304, "2,304")
    ];

    public static IReadOnlyList<LlmChoice> ReceiptSplitMinHeightPixels { get; } =
    [
        new(1120, "1,120 px"),
        new(1344, "1,344 px"),
        new(1400, "1,400 px"),
        new(1536, "1,536 px"),
        new(1792, "1,792 px"),
        new(2016, "2,016 px")
    ];

    public static IReadOnlyList<LlmChoice> ReceiptSplitOverlapPixels { get; } =
    [
        new(112, "112 px"),
        new(168, "168 px"),
        new(224, "224 px"),
        new(280, "280 px"),
        new(336, "336 px")
    ];

    public static IReadOnlyList<LlmChoice> NumCtx { get; } =
    [
        new(0, "Provider default"),
        new(4096, "4,096"),
        new(8192, "8,192"),
        new(16384, "16,384")
    ];

    public static IReadOnlyList<LlmChoice> VisionMaxTokens { get; } =
    [
        new(1024, "1,024"),
        new(2048, "2,048"),
        new(4096, "4,096"),
        new(8192, "8,192")
    ];

    public static IReadOnlyList<LlmChoice> MaxReceiptImages { get; } =
    [
        new(5, "5"),
        new(10, "10"),
        new(20, "20"),
        new(40, "40")
    ];

    public static IReadOnlyList<LlmChoice> MaxPdfPages { get; } =
    [
        new(10, "10"),
        new(20, "20"),
        new(30, "30"),
        new(50, "50"),
        new(100, "100")
    ];

    public static int Snap(int value, IReadOnlyList<LlmChoice> choices, int defaultValue)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
            return defaultValue;

        var best = defaultValue;
        var bestDistance = int.MaxValue;
        foreach (var choice in choices)
        {
            if (choice.Value == value)
                return value;

            var distance = Math.Abs(choice.Value - value);
            if (distance < bestDistance
                || (distance == bestDistance && choice.Value == defaultValue))
            {
                best = choice.Value;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static int SnapOrDefault(int value, IReadOnlyList<LlmChoice> choices, int defaultValue) =>
        value <= 0 ? defaultValue : Snap(value, choices, defaultValue);

    public static int SnapMaxEdge(int value) =>
        value <= 0 ? 0 : Snap(value, Positive(MaxReceiptImageEdgePixels), 1536);

    public static int SnapNumCtx(int value) =>
        value <= 0 ? 0 : Snap(value, Positive(NumCtx), 8192);

    public static int SnapFallback(int value, int maxEdgePixels)
    {
        var snapped = SnapOrDefault(value, FallbackMaxEdgePixels, 672);
        if (maxEdgePixels <= 0 || snapped < maxEdgePixels)
            return snapped;

        return LargestBelow(FallbackMaxEdgePixels, maxEdgePixels) ?? FallbackMaxEdgePixels[0].Value;
    }

    public static int SnapTallEdge(int value, int maxEdgePixels)
    {
        var snapped = SnapOrDefault(value, MaxTallReceiptEdgePixels, 2016);
        if (maxEdgePixels <= 0 || snapped >= maxEdgePixels)
            return snapped;

        return SmallestAtLeast(MaxTallReceiptEdgePixels, maxEdgePixels) ?? MaxTallReceiptEdgePixels[^1].Value;
    }

    private static IReadOnlyList<LlmChoice> Positive(IReadOnlyList<LlmChoice> choices) =>
        choices.Where(choice => choice.Value > 0).ToList();

    private static int? LargestBelow(IReadOnlyList<LlmChoice> choices, int limit)
    {
        int? best = null;
        foreach (var choice in choices)
        {
            if (choice.Value < limit && (best is null || choice.Value > best.Value))
                best = choice.Value;
        }

        return best;
    }

    private static int? SmallestAtLeast(IReadOnlyList<LlmChoice> choices, int limit)
    {
        int? best = null;
        foreach (var choice in choices)
        {
            if (choice.Value >= limit && (best is null || choice.Value < best.Value))
                best = choice.Value;
        }

        return best;
    }
}
