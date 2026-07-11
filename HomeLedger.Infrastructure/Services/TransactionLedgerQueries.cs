using HomeLedger.Core.Entities;

namespace HomeLedger.Infrastructure.Services;

public static class TransactionLedgerQueries
{
    public static IQueryable<Transaction> WhereActiveForReporting(this IQueryable<Transaction> query) =>
        query.Where(t => t.SupersededByTransactionId == null && t.Kind != TransactionKind.Receipt);

    public static IQueryable<Transaction> WhereVisibleInLedgerList(this IQueryable<Transaction> query, bool includeSuperseded) =>
        includeSuperseded
            ? query
            : query.Where(t => t.SupersededByTransactionId == null);

    public static bool CountsTowardLedgerTotals(this Transaction transaction) =>
        transaction.SupersededByTransactionId is null && transaction.Kind != TransactionKind.Receipt;
}
