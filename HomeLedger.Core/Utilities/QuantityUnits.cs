using System.Globalization;

namespace HomeLedger.Core.Utilities;

public static class QuantityUnits
{
    public const string Each = "ea";
    public const string Gram = "g";
    public const string Kilogram = "kg";
    public const string Ounce = "oz";
    public const string Pound = "lb";

    public static readonly IReadOnlyList<string> Known = [Each, Gram, Kilogram, Ounce, Pound];

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var key = value.Trim().ToLowerInvariant().TrimEnd('.');
        return key switch
        {
            "ea" or "each" or "unit" or "units" or "ct" or "count"
                or "pc" or "pcs" or "piece" or "pieces" or "item" or "items" or "x" => Each,
            "g" or "gm" or "gram" or "grams" => Gram,
            "kg" or "kgs" or "kilogram" or "kilograms" => Kilogram,
            "oz" or "ounce" or "ounces" => Ounce,
            "lb" or "lbs" or "pound" or "pounds" => Pound,
            _ => null
        };
    }

    public static string FormatQuantity(decimal quantity)
    {
        if (quantity == decimal.Truncate(quantity))
            return decimal.Truncate(quantity).ToString(CultureInfo.InvariantCulture);

        return quantity.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string FormatMeasure(decimal quantity, string? unit)
    {
        var normalized = Normalize(unit);
        return normalized is null
            ? FormatQuantity(quantity)
            : $"{FormatQuantity(quantity)} {normalized}";
    }

    public static string? FormatSummary(decimal? quantity, string? unit, decimal? unitPrice)
    {
        var parts = new List<string>();
        if (quantity is > 0)
            parts.Add(FormatMeasure(quantity.Value, unit));
        if (unitPrice is > 0)
            parts.Add(unitPrice.Value.ToString("C"));

        return parts.Count == 0 ? null : string.Join(" @ ", parts);
    }

    public static string StripLeadingQuantity(string description, decimal? quantity)
    {
        description = description.Trim();
        if (quantity is null or <= 0)
            return description;

        var qtyLabel = FormatQuantity(quantity.Value);
        string[] prefixes =
        [
            $"{qtyLabel}x",
            $"{qtyLabel} x",
            $"{qtyLabel}×",
            $"{qtyLabel} ×"
        ];

        foreach (var prefix in prefixes)
        {
            if (!description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = description[prefix.Length..].TrimStart(' ', '-', ':');
            if (rest.Length > 0)
                return rest;
        }

        return description;
    }
}
