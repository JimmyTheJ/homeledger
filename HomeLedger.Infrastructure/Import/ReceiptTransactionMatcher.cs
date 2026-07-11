using HomeLedger.Core.Entities;
using HomeLedger.Infrastructure.Import;

namespace HomeLedger.Infrastructure.Import;

public record ReceiptMatchCandidate(
    int TransactionId,
    TransactionKind Kind,
    DateOnly Date,
    decimal Amount,
    string? Merchant,
    string? Notes);

public static class ReceiptTransactionMatcher
{
    public const decimal AmountTolerance = 0.02m;
    public const int DateToleranceDays = 1;

    public static bool AmountsMatch(decimal left, decimal right) =>
        Math.Abs(left - right) <= AmountTolerance;

    public static bool DatesMatch(DateOnly left, DateOnly right) =>
        Math.Abs(left.DayNumber - right.DayNumber) <= DateToleranceDays;

    public static bool CsvLineMatchesReceipt(
        ImportDuplicateMatcher.TransactionFingerprint csvLine,
        ReceiptMatchCandidate receipt)
    {
        if (receipt.Kind != TransactionKind.Receipt)
            return false;

        if (!DatesMatch(csvLine.Date, receipt.Date) || !AmountsMatch(csvLine.Amount, receipt.Amount))
            return false;

        return MerchantCompatible(csvLine.Description, receipt.Merchant, receipt.Notes);
    }

    public static bool ReceiptTotalMatchesStandardTransaction(
        DateOnly receiptDate,
        decimal receiptTotal,
        string? receiptMerchant,
        ReceiptMatchCandidate standard)
    {
        if (standard.Kind != TransactionKind.Standard)
            return false;

        if (!DatesMatch(receiptDate, standard.Date) || !AmountsMatch(receiptTotal, standard.Amount))
            return false;

        return MerchantCompatible(standard.Notes ?? standard.Merchant, receiptMerchant, standard.Merchant);
    }

    public static ReceiptMatchCandidate ToCandidate(Transaction transaction) =>
        new(
            transaction.Id,
            transaction.Kind,
            transaction.Date,
            transaction.Amount,
            transaction.Merchant,
            transaction.Notes);

    public static string DescribeReceiptMatch(ReceiptMatchCandidate receipt) =>
        $"receipt #{receipt.TransactionId}" +
        (string.IsNullOrWhiteSpace(receipt.Merchant) ? "" : $" ({receipt.Merchant})") +
        $" on {receipt.Date:yyyy/MM/dd} for {receipt.Amount:C}";

    public static string DescribeStandardMatch(ReceiptMatchCandidate standard) =>
        $"transaction #{standard.TransactionId}" +
        (string.IsNullOrWhiteSpace(standard.Merchant) ? "" : $" ({standard.Merchant})") +
        (string.IsNullOrWhiteSpace(standard.Notes) ? "" : $" — {standard.Notes}") +
        $" on {standard.Date:yyyy/MM/dd} for {standard.Amount:C}";

    private static bool MerchantCompatible(string? csvDescription, string? receiptMerchant, string? receiptNotes)
    {
        if (string.IsNullOrWhiteSpace(receiptMerchant))
            return true;

        var merchant = Normalize(receiptMerchant);
        var haystack = Normalize($"{csvDescription} {receiptNotes}");
        if (string.IsNullOrWhiteSpace(haystack))
            return true;

        return haystack.Contains(merchant, StringComparison.Ordinal)
            || merchant.Contains(Normalize(csvDescription), StringComparison.Ordinal);
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
