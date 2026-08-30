using HomeLedger.Infrastructure.Import;
using HomeLedger.Infrastructure.Llm;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class ReceiptSplitMergerTests
{
    [Fact]
    public void Combine_drops_overlapping_suffix_prefix_lines()
    {
        var top = Receipt(
            "Walmart",
            new DateOnly(2026, 8, 15),
            "R1",
            Line("Milk", -4.99m),
            Line("Bread", -3.49m),
            Line("Eggs", -6.15m));
        var bottom = Receipt(
            "Unknown merchant",
            null,
            null,
            Line("eggs", -6.15m),
            Line("Apples", -2.10m),
            Line("Butter", -5.00m));

        var merged = ReceiptSplitMerger.Combine(top, bottom);

        Assert.NotNull(merged);
        Assert.Equal("Walmart", merged.Merchant);
        Assert.Equal(new DateOnly(2026, 8, 15), merged.ReceiptDate);
        Assert.Equal("R1", merged.ExternalId);
        Assert.Equal(["Milk", "Bread", "Eggs", "Apples", "Butter"], merged.LineItems.Select(l => l.Description));
        Assert.All(merged.LineItems, line => Assert.Equal(new DateOnly(2026, 8, 15), line.Date));
    }

    [Fact]
    public void Combine_keeps_both_halves_when_there_is_no_overlap()
    {
        var top = Receipt("Costco", new DateOnly(2026, 8, 1), null, Line("Chicken", -12.00m));
        var bottom = Receipt("Costco", new DateOnly(2026, 8, 1), null, Line("Rice", -8.00m));

        var merged = ReceiptSplitMerger.Combine(top, bottom);

        Assert.NotNull(merged);
        Assert.Equal(2, merged.LineItems.Count);
        Assert.Equal("Chicken", merged.LineItems[0].Description);
        Assert.Equal("Rice", merged.LineItems[1].Description);
    }

    [Fact]
    public void Combine_returns_the_other_half_when_one_is_empty()
    {
        var top = Receipt("Walmart", new DateOnly(2026, 8, 15), null, Line("Milk", -4.99m));

        Assert.Same(top, ReceiptSplitMerger.Combine(top, null));
        Assert.Equal("Milk", Assert.Single(ReceiptSplitMerger.Combine(null, top)!.LineItems).Description);
    }

    [Fact]
    public void SameLine_treats_punctuation_and_typos_as_duplicates()
    {
        var left = Line("KENDAMIL INF F", -56.99m);
        var right = Line("KENDAMIL INF. F", -56.99m);

        Assert.True(ReceiptSplitMerger.SameLine(left, right));
        Assert.False(ReceiptSplitMerger.SameLine(left, Line("KENDAMIL INF F", -13.99m)));
    }

    private static ExtractedReceipt Receipt(
        string merchant,
        DateOnly? date,
        string? externalId,
        params ExtractedReceiptLine[] lines) =>
        new(merchant, date, externalId, lines);

    private static ExtractedReceiptLine Line(string description, decimal amount) =>
        new(new DateOnly(2026, 1, 1), amount, description, "Groceries");
}
