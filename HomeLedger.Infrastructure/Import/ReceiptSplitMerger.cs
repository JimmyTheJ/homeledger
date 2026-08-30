using HomeLedger.Infrastructure.Llm;

namespace HomeLedger.Infrastructure.Import;

internal static class ReceiptSplitMerger
{
    internal const string UnknownMerchant = "Unknown merchant";

    public static ExtractedReceipt? Combine(ExtractedReceipt? top, ExtractedReceipt? bottom)
    {
        if (top is null)
            return bottom;
        if (bottom is null || bottom.LineItems.Count == 0)
            return top;
        if (top.LineItems.Count == 0)
            return CombineMetadata(top, bottom, bottom.LineItems);

        // Overlap is visual context only. Each tile owns one side of the cut, so identical
        // product rows (five "Bodysuit 1.50") must be kept, not treated as seam duplicates.
        return CombineMetadata(top, bottom, [.. top.LineItems, .. bottom.LineItems]);
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
}
