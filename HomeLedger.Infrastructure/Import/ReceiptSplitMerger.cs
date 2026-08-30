using HomeLedger.Infrastructure.Llm;

namespace HomeLedger.Infrastructure.Import;

internal static class ReceiptSplitMerger
{
    internal const int OverlapLineWindow = 8;
    internal const string UnknownMerchant = "Unknown merchant";

    public static ExtractedReceipt? Combine(ExtractedReceipt? top, ExtractedReceipt? bottom)
    {
        if (top is null)
            return bottom;
        if (bottom is null || bottom.LineItems.Count == 0)
            return top;
        if (top.LineItems.Count == 0)
            return CombineMetadata(top, bottom, bottom.LineItems);

        var lines = DedupeOverlap(top.LineItems, bottom.LineItems);
        return CombineMetadata(top, bottom, lines);
    }

    internal static IReadOnlyList<ExtractedReceiptLine> DedupeOverlap(
        IReadOnlyList<ExtractedReceiptLine> top,
        IReadOnlyList<ExtractedReceiptLine> bottom)
    {
        var skip = OverlapSkipCount(top, bottom);
        if (skip == 0)
            return [.. top, .. bottom];

        return [.. top, .. bottom.Skip(skip)];
    }

    internal static int OverlapSkipCount(
        IReadOnlyList<ExtractedReceiptLine> top,
        IReadOnlyList<ExtractedReceiptLine> bottom)
    {
        var window = Math.Min(OverlapLineWindow, Math.Min(top.Count, bottom.Count));
        for (var n = window; n >= 1; n--)
        {
            if (SequencesMatch(top.TakeLast(n), bottom.Take(n)))
                return n;
        }

        return ConsumedOverlapSkipCount(top.TakeLast(window).ToList(), bottom, window);
    }

    private static int ConsumedOverlapSkipCount(
        List<ExtractedReceiptLine> unused,
        IReadOnlyList<ExtractedReceiptLine> bottom,
        int window)
    {
        var skip = 0;
        for (var i = 0; i < window; i++)
        {
            var match = unused.FindIndex(line => SameLine(line, bottom[i]));
            if (match < 0)
                break;

            unused.RemoveAt(match);
            skip = i + 1;
        }

        return skip;
    }

    internal static bool SameLine(ExtractedReceiptLine left, ExtractedReceiptLine right)
    {
        if (left.Amount != right.Amount)
            return false;

        var leftName = NormalizeDescription(left.Description);
        var rightName = NormalizeDescription(right.Description);
        if (leftName.Length == 0 || rightName.Length == 0)
            return false;
        if (leftName == rightName)
            return true;
        if (leftName.Length >= 6 && rightName.Length >= 6
            && (leftName.Contains(rightName, StringComparison.Ordinal)
                || rightName.Contains(leftName, StringComparison.Ordinal)))
        {
            return true;
        }

        return Levenshtein(leftName, rightName) <= 2;
    }

    internal static string NormalizeDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        var chars = description
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static ExtractedReceipt CombineMetadata(
        ExtractedReceipt top,
        ExtractedReceipt bottom,
        IReadOnlyList<ExtractedReceiptLine> lines)
    {
        var merchant = PreferMerchant(top.Merchant, bottom.Merchant);
        var date = top.ReceiptDate ?? bottom.ReceiptDate;
        var dated = date is null
            ? lines
            : lines.Select(line => line with { Date = date.Value }).ToList();

        return new ExtractedReceipt(
            merchant,
            date,
            top.ExternalId ?? bottom.ExternalId,
            dated,
            bottom.Subtotal ?? top.Subtotal);
    }

    private static string PreferMerchant(string top, string bottom)
    {
        if (!IsUnknownMerchant(top))
            return top;
        if (!IsUnknownMerchant(bottom))
            return bottom;
        return top;
    }

    private static bool IsUnknownMerchant(string merchant) =>
        string.IsNullOrWhiteSpace(merchant)
        || merchant.Equals(UnknownMerchant, StringComparison.OrdinalIgnoreCase);

    private static bool SequencesMatch(
        IEnumerable<ExtractedReceiptLine> left,
        IEnumerable<ExtractedReceiptLine> right)
    {
        using var leftEnum = left.GetEnumerator();
        using var rightEnum = right.GetEnumerator();
        while (true)
        {
            var hasLeft = leftEnum.MoveNext();
            var hasRight = rightEnum.MoveNext();
            if (hasLeft != hasRight)
                return false;
            if (!hasLeft)
                return true;
            if (!SameLine(leftEnum.Current, rightEnum.Current))
                return false;
        }
    }

    private static int Levenshtein(string left, string right)
    {
        if (left == right)
            return 0;
        if (left.Length == 0)
            return right.Length;
        if (right.Length == 0)
            return left.Length;

        var prev = new int[right.Length + 1];
        var next = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            next[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                next[j] = Math.Min(
                    Math.Min(next[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, next) = (next, prev);
        }

        return prev[right.Length];
    }
}
