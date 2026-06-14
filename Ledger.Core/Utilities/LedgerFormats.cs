namespace Ledger.Core.Utilities;

public static class LedgerFormats
{
    public const string DatePattern = "yyyy/MM/dd";

    public static string FormatDate(DateOnly date) => date.ToString(DatePattern);

    public static string? FormatDate(DateOnly? date) => date is null ? null : FormatDate(date.Value);

    public static bool TryParseDate(string? input, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var normalized = input.Trim().Replace('-', '/');
        return DateOnly.TryParseExact(normalized, DatePattern, null, System.Globalization.DateTimeStyles.None, out date)
            || DateOnly.TryParse(normalized, out date);
    }
}
