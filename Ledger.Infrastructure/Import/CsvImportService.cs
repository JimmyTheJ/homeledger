using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Ledger.Core.Entities;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Import;

public record CsvImportRow(
    DateOnly Date,
    decimal Amount,
    string Description,
    string? ExternalId);

public interface ICsvImportService
{
    Task<ImportBatch> CreateBatchAsync(
        Stream csvStream,
        string fileName,
        int accountId,
        int ledgerEntityId,
        bool autoAccept,
        CancellationToken ct = default);

    Task<ImportItem?> GetNextPendingItemAsync(string batchId, CancellationToken ct = default);
    Task<Transaction?> AcceptItemAsync(AcceptImportItemRequest request, CancellationToken ct = default);
    Task SkipItemAsync(int itemId, CancellationToken ct = default);
    Task CompleteBatchIfDoneAsync(string batchId, CancellationToken ct = default);
}

public record AcceptImportItemRequest(
    int ItemId,
    DateOnly Date,
    decimal Amount,
    int CategoryId,
    int LedgerEntityId,
    int? AccountId,
    string Notes);

public class CsvImportService : ICsvImportService
{
    private readonly LedgerDbContext _db;
    private readonly ITransactionCategorizer _categorizer;

    public CsvImportService(LedgerDbContext db, ITransactionCategorizer categorizer)
    {
        _db = db;
        _categorizer = categorizer;
    }

    public async Task<ImportBatch> CreateBatchAsync(
        Stream csvStream,
        string fileName,
        int accountId,
        int ledgerEntityId,
        bool autoAccept,
        CancellationToken ct = default)
    {
        var rows = ParseCsv(csvStream);
        var categories = await _db.Categories.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);

        var batch = new ImportBatch
        {
            FileName = fileName,
            AccountId = accountId,
            LedgerEntityId = ledgerEntityId,
            AutoAccept = autoAccept,
            Status = autoAccept ? ImportBatchStatus.Pending : ImportBatchStatus.Reviewing
        };

        foreach (var row in rows)
        {
            var suggestion = await _categorizer.SuggestAsync(row.Description, row.Amount, categories, ct);
            batch.Items.Add(new ImportItem
            {
                Date = row.Date,
                Amount = row.Amount,
                Description = row.Description,
                ExternalId = row.ExternalId,
                SuggestedCategoryId = suggestion.CategoryId,
                SuggestedNotes = suggestion.Notes ?? row.Description
            });
        }

        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        if (autoAccept)
        {
            foreach (var item in batch.Items)
            {
                if (item.SuggestedCategoryId is null)
                    continue;

                await AcceptItemAsync(new AcceptImportItemRequest(
                    item.Id,
                    item.Date,
                    item.Amount,
                    item.SuggestedCategoryId.Value,
                    ledgerEntityId,
                    accountId,
                    item.SuggestedNotes ?? item.Description), ct);
            }

            batch.Status = ImportBatchStatus.Completed;
            batch.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return batch;
    }

    public Task<ImportItem?> GetNextPendingItemAsync(string batchId, CancellationToken ct = default) =>
        _db.ImportItems
            .Include(i => i.SuggestedCategory)
            .Where(i => i.ImportBatchId == batchId && i.Status == ImportItemStatus.Pending)
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<Transaction?> AcceptItemAsync(AcceptImportItemRequest request, CancellationToken ct = default)
    {
        var item = await _db.ImportItems
            .Include(i => i.ImportBatch)
            .FirstOrDefaultAsync(i => i.Id == request.ItemId, ct);

        if (item is null || item.Status != ImportItemStatus.Pending)
            return null;

        if (!string.IsNullOrWhiteSpace(item.ExternalId) && request.AccountId is not null)
        {
            var exists = await _db.Transactions.AnyAsync(
                t => t.ExternalId == item.ExternalId && t.AccountId == request.AccountId, ct);
            if (exists)
            {
                item.Status = ImportItemStatus.Skipped;
                await _db.SaveChangesAsync(ct);
                return null;
            }
        }

        var transaction = new Transaction
        {
            Date = request.Date,
            Amount = request.Amount,
            CategoryId = request.CategoryId,
            LedgerEntityId = request.LedgerEntityId,
            AccountId = request.AccountId,
            Notes = request.Notes,
            ExternalId = item.ExternalId
        };

        _db.Transactions.Add(transaction);
        item.Status = ImportItemStatus.Accepted;
        await _db.SaveChangesAsync(ct);

        item.ResultingTransactionId = transaction.Id;
        await _db.SaveChangesAsync(ct);
        await CompleteBatchIfDoneAsync(item.ImportBatchId, ct);
        return transaction;
    }

    public async Task SkipItemAsync(int itemId, CancellationToken ct = default)
    {
        var item = await _db.ImportItems.FindAsync([itemId], ct);
        if (item is null) return;

        item.Status = ImportItemStatus.Skipped;
        await _db.SaveChangesAsync(ct);
        await CompleteBatchIfDoneAsync(item.ImportBatchId, ct);
    }

    public async Task CompleteBatchIfDoneAsync(string batchId, CancellationToken ct = default)
    {
        var hasPending = await _db.ImportItems
            .AnyAsync(i => i.ImportBatchId == batchId && i.Status == ImportItemStatus.Pending, ct);

        if (hasPending) return;

        var batch = await _db.ImportBatches.FindAsync([batchId], ct);
        if (batch is null) return;

        batch.Status = ImportBatchStatus.Completed;
        batch.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static List<CsvImportRow> ParseCsv(Stream csvStream)
    {
        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => args.Header.ToLowerInvariant().Replace(" ", "").Replace("_", "")
        });

        if (!csv.Read() || !csv.ReadHeader())
            return [];

        var headers = csv.HeaderRecord ?? [];
        var dateCol = FindColumn(headers, "date", "transactiondate", "posteddate", "postingdate");
        var amountCol = FindColumn(headers, "amount", "cad", "value", "debit", "credit");
        var descCol = FindColumn(headers, "description", "memo", "details", "narrative", "name");
        var idCol = FindColumn(headers, "id", "transactionid", "referencenumber", "referenceno");

        var rows = new List<CsvImportRow>();
        while (csv.Read())
        {
            var date = ParseDate(csv, dateCol);
            var amount = ParseAmount(csv, headers, amountCol);
            var description = descCol >= 0 ? csv.GetField(descCol)?.Trim() ?? "" : "";
            var externalId = idCol >= 0 ? csv.GetField(idCol)?.Trim() : null;

            if (date is null || amount is null || string.IsNullOrWhiteSpace(description))
                continue;

            rows.Add(new CsvImportRow(date.Value, amount.Value, description, externalId));
        }

        return rows;
    }

    private static int FindColumn(string[] headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var normalized = headers[i].ToLowerInvariant().Replace(" ", "").Replace("_", "");
            if (candidates.Any(c => normalized.Contains(c, StringComparison.Ordinal)))
                return i;
        }
        return -1;
    }

    private static DateOnly? ParseDate(CsvReader csv, int column)
    {
        if (column < 0) return null;
        var raw = csv.GetField(column)?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);

        return null;
    }

    private static decimal? ParseAmount(CsvReader csv, string[] headers, int amountCol)
    {
        if (amountCol >= 0)
        {
            var raw = csv.GetField(amountCol)?.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                return amount;
        }

        var debitCol = FindColumn(headers, "debit");
        var creditCol = FindColumn(headers, "credit");
        decimal? debit = null, credit = null;

        if (debitCol >= 0)
        {
            var raw = csv.GetField(debitCol)?.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d != 0)
                debit = d;
        }

        if (creditCol >= 0)
        {
            var raw = csv.GetField(creditCol)?.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var c) && c != 0)
                credit = c;
        }

        if (credit.HasValue) return credit.Value;
        if (debit.HasValue) return -Math.Abs(debit.Value);
        return null;
    }
}
