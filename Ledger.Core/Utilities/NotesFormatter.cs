namespace Ledger.Core.Utilities;

public static class NotesFormatter
{
    /// <summary>
    /// Splits free-form notes on sentence boundaries (periods), matching the spreadsheet convention.
    /// </summary>
    public static IReadOnlyList<string> ParseSegments(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return [];

        return notes
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment.Length > 0)
            .ToList();
    }
}
