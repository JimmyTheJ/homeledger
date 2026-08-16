using HomeLedger.Core.Utilities;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class QuantityUnitsTests
{
    [Theory]
    [InlineData("EACH", "ea")]
    [InlineData("pcs", "ea")]
    [InlineData("kg", "kg")]
    [InlineData("Kilograms", "kg")]
    [InlineData("lbs", "lb")]
    [InlineData("ounces", "oz")]
    [InlineData("grams", "g")]
    public void Normalize_maps_receipt_unit_aliases(string input, string expected)
    {
        Assert.Equal(expected, QuantityUnits.Normalize(input));
    }

    [Fact]
    public void Normalize_returns_null_for_unknown_units()
    {
        Assert.Null(QuantityUnits.Normalize("bunch"));
        Assert.Null(QuantityUnits.Normalize(" "));
    }

    [Fact]
    public void FormatMeasure_includes_normalized_unit()
    {
        Assert.Equal("5 ea", QuantityUnits.FormatMeasure(5, "each"));
        Assert.Equal("0.64 kg", QuantityUnits.FormatMeasure(0.640m, "kg"));
    }

    [Fact]
    public void StripLeadingQuantity_removes_count_prefix()
    {
        Assert.Equal("KENDAMIL INF F", QuantityUnits.StripLeadingQuantity("5x KENDAMIL INF F", 5));
        Assert.Equal("KENDAMIL INF F", QuantityUnits.StripLeadingQuantity("5 x KENDAMIL INF F", 5));
        Assert.Equal("KENDAMIL INF F", QuantityUnits.StripLeadingQuantity("KENDAMIL INF F", 5));
    }
}
