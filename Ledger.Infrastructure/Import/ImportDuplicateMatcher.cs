using Ledger.Core.Entities;
using System.Globalization;

namespace Ledger.Infrastructure.Import;

internal static class ImportDuplicateMatcher
{
    internal readonly record struct TransactionFingerprint(
        DateOnly Date,
        decimal Amount,
        string Description,
        string? ExternalId);

    public static bool SameRow(TransactionFingerprint a, TransactionFingerprint b)
    {
        if (a.Date != b.Date || a.Amount != b.Amount)
            return false;

        if (!string.IsNullOrWhiteSpace(a.ExternalId) && !string.IsNullOrWhiteSpace(b.ExternalId))
            return string.Equals(a.ExternalId, b.ExternalId, StringComparison.OrdinalIgnoreCase);

        return Normalize(a.Description) == Normalize(b.Description);
    }

    public static bool MatchesTransaction(TransactionFingerprint item, Transaction transaction)
    {
        if (transaction.AccountId is null)
            return false;

        if (!string.IsNullOrWhiteSpace(item.ExternalId) &&
            !string.IsNullOrWhiteSpace(transaction.ExternalId) &&
            string.Equals(item.ExternalId, transaction.ExternalId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.Date != transaction.Date || item.Amount != transaction.Amount)
            return false;

        var description = Normalize(item.Description);
        var notes = Normalize(transaction.Notes ?? "");
        return description == notes
            || (!string.IsNullOrEmpty(description) && notes.Contains(description, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(notes) && description.Contains(notes, StringComparison.Ordinal));
    }

    public static TransactionFingerprint FromItem(ImportItem item) =>
        new(item.Date, item.Amount, item.Description, item.ExternalId);

    public static TransactionFingerprint FromRow(CsvImportRow row) =>
        new(row.Date, row.Amount, row.Description, row.ExternalId);

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();
}
