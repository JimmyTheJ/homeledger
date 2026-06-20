using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Infrastructure.Import;

public interface ITransferPairMatcher
{
    Task<string?> FindExistingPairSkipReasonAsync(
        int ledgerEntityId,
        int accountId,
        DateOnly date,
        decimal amount,
        CancellationToken ct = default);

    string? FindBatchPairSkipReason(
        ImportItem item,
        int itemIndex,
        IReadOnlyList<ImportItem> batchItems,
        HashSet<int> skippedIndices);
}

public class TransferPairMatcher : ITransferPairMatcher
{
    private readonly HomeLedgerDbContext _db;

    public TransferPairMatcher(HomeLedgerDbContext db) => _db = db;

    public async Task<string?> FindExistingPairSkipReasonAsync(
        int ledgerEntityId,
        int accountId,
        DateOnly date,
        decimal amount,
        CancellationToken ct = default)
    {
        if (amount == 0)
            return null;

        var opposite = -amount;
        var minDate = date.AddDays(-1);
        var maxDate = date.AddDays(1);

        var hasMatch = await _db.Transactions.AsNoTracking().AnyAsync(t =>
            t.LedgerEntityId == ledgerEntityId
            && t.AccountId != accountId
            && t.AccountId != null
            && t.Date >= minDate
            && t.Date <= maxDate
            && t.Amount == opposite, ct);

        return hasMatch ? ImportSkipReasons.PairedTransfer : null;
    }

    public string? FindBatchPairSkipReason(
        ImportItem item,
        int itemIndex,
        IReadOnlyList<ImportItem> batchItems,
        HashSet<int> skippedIndices)
    {
        if (item.Amount == 0)
            return null;

        var opposite = -item.Amount;
        var minDate = item.Date.AddDays(-1);
        var maxDate = item.Date.AddDays(1);

        for (var i = 0; i < batchItems.Count; i++)
        {
            if (i == itemIndex || skippedIndices.Contains(i))
                continue;

            var other = batchItems[i];
            if (other.Status != ImportItemStatus.Pending)
                continue;

            if (other.Date >= minDate && other.Date <= maxDate && other.Amount == opposite)
                return ImportSkipReasons.PairedTransfer;
        }

        return null;
    }
}
