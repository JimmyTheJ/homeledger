using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using HomeLedger.Core.Configuration;
using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Import;

public record CsvImportRow(
    DateOnly Date,
    decimal Amount,
    string Description,
    string? ExternalId,
    string? Merchant = null,
    string? SuggestedCategoryName = null,
    string? SourceFileName = null);

public interface ICsvImportService
{
    Task<ImportBatch> CreateBatchAsync(
        Stream csvStream,
        string fileName,
        long fileSizeBytes,
        string fileSha256,
        int accountId,
        int ledgerEntityId,
        bool autoAccept,
        CancellationToken ct = default);

    Task<ImportBatch> CreateBatchFromRowsAsync(
        IReadOnlyList<CsvImportRow> rows,
        string fileName,
        long fileSizeBytes,
        string fileSha256,
        int accountId,
        int ledgerEntityId,
        bool autoAccept,
        bool pdfExtractedWithLlm = false,
        ImportKind importKind = ImportKind.Csv,
        string? batchMerchant = null,
        string? sourcePath = null,
        CancellationToken ct = default);

    Task<int> CountPendingReceiptReviewsAsync(CancellationToken ct = default);

    Task<ImportBatch?> FindPriorImportAsync(string fileSha256, long fileSizeBytes, int accountId, CancellationToken ct = default);
    Task<ImportItem?> GetNextPendingItemAsync(string batchId, CancellationToken ct = default);
    Task<AcceptItemResult> AcceptItemAsync(AcceptImportItemRequest request, CancellationToken ct = default);
    Task SkipItemAsync(int itemId, CancellationToken ct = default);
    Task CompleteBatchIfDoneAsync(string batchId, CancellationToken ct = default);
    Task<DeleteBatchResult> DeleteIncompleteBatchAsync(string batchId, CancellationToken ct = default);
}

public enum DeleteBatchStatus
{
    Deleted,
    NotFound,
    AlreadyCompleted
}

public record DeleteBatchResult(DeleteBatchStatus Status, int TransactionsKept);

public enum ImportAcceptStatus
{
    Accepted,
    SkippedDuplicate,
    InvalidState
}

public record AcceptItemResult(ImportAcceptStatus Status, string? Message, Transaction? Transaction);

public record AcceptImportItemRequest(
    int ItemId,
    DateOnly Date,
    decimal Amount,
    int CategoryId,
    int LedgerEntityId,
    int? AccountId,
    string? Notes);

public static class ImportFileFingerprint
{
    public static async Task<(byte[] Content, string Sha256Hex)> ReadAndHashAsync(Stream stream, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var content = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return (content, hash);
    }

    public static (string Sha256Hex, long TotalSize) HashCombined(IReadOnlyList<byte[]> filesInOrder)
    {
        using var ms = new MemoryStream();
        foreach (var file in filesInOrder)
            ms.Write(file);

        var content = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return (hash, content.Length);
    }
}

public class CsvImportService : ICsvImportService
{
    private readonly HomeLedgerDbContext _db;
    private readonly ITransactionCategorizer _categorizer;
    private readonly IImportProfileService _profiles;
    private readonly IImportSkipRuleMatcher _ruleMatcher;
    private readonly ITransferPairMatcher _transferMatcher;
    private readonly IImportRowClassifier _rowClassifier;
    private readonly ILlmClient _llmClient;
    private readonly LlmSettings _settings;
    private readonly ILogger<CsvImportService> _logger;

    public CsvImportService(
        HomeLedgerDbContext db,
        ITransactionCategorizer categorizer,
        IImportProfileService profiles,
        IImportSkipRuleMatcher ruleMatcher,
        ITransferPairMatcher transferMatcher,
        IImportRowClassifier rowClassifier,
        ILlmClient llmClient,
        IOptions<LlmSettings> settings,
        ILogger<CsvImportService> logger)
    {
        _db = db;
        _categorizer = categorizer;
        _profiles = profiles;
        _ruleMatcher = ruleMatcher;
        _transferMatcher = transferMatcher;
        _rowClassifier = rowClassifier;
        _llmClient = llmClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<ImportBatch?> FindPriorImportAsync(string fileSha256, long fileSizeBytes, int accountId, CancellationToken ct = default) =>
        _db.ImportBatches
            .AsNoTracking()
            .Where(b => b.FileSha256 == fileSha256
                && b.FileSizeBytes == fileSizeBytes
                && b.AccountId == accountId
                && b.Status == ImportBatchStatus.Completed)
            .OrderByDescending(b => b.CompletedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<ImportBatch> CreateBatchAsync(
        Stream csvStream,
        string fileName,
        long fileSizeBytes,
        string fileSha256,
        int accountId,
        int ledgerEntityId,
        bool autoAccept,
        CancellationToken ct = default)
    {
        var rows = ParseCsv(csvStream);
        return await CreateBatchFromRowsAsync(
            rows, fileName, fileSizeBytes, fileSha256, accountId, ledgerEntityId, autoAccept,
            pdfExtractedWithLlm: false, importKind: ImportKind.Csv, ct: ct);
    }

    public Task<int> CountPendingReceiptReviewsAsync(CancellationToken ct = default) =>
        _db.ImportBatches.CountAsync(
            b => b.Status == ImportBatchStatus.Reviewing
                && (b.ImportKind == ImportKind.Receipt || b.ImportKind == ImportKind.WatchedReceipt),
            ct);

    public async Task<ImportBatch> CreateBatchFromRowsAsync(
        IReadOnlyList<CsvImportRow> rows,
        string fileName,
        long fileSizeBytes,
        string fileSha256,
        int accountId,
        int ledgerEntityId,
        bool autoAccept,
        bool pdfExtractedWithLlm = false,
        ImportKind importKind = ImportKind.Csv,
        string? batchMerchant = null,
        string? sourcePath = null,
        CancellationToken ct = default)
    {
        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive && (c.LedgerEntityId == null || c.LedgerEntityId == ledgerEntityId))
            .ToListAsync(ct);

        var profile = await _profiles.GetForAccountAsync(accountId, ledgerEntityId, ct);

        var batch = new ImportBatch
        {
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            FileSha256 = fileSha256,
            AccountId = accountId,
            LedgerEntityId = ledgerEntityId,
            AutoAccept = autoAccept,
            Status = autoAccept ? ImportBatchStatus.Pending : ImportBatchStatus.Reviewing,
            PdfExtractedWithLlm = pdfExtractedWithLlm,
            ImportKind = importKind,
            Merchant = batchMerchant,
            SourcePath = sourcePath
        };

        PopulateLlmSnapshot(batch, profile);

        var items = new List<ImportItem>();
        foreach (var row in rows)
        {
            var suggestion = await ResolveCategorySuggestionAsync(row, categories, ct);
            if (suggestion.Source == "llm")
                batch.LlmCategorizedCount++;

            items.Add(new ImportItem
            {
                Date = row.Date,
                Amount = row.Amount,
                Description = row.Description,
                ExternalId = row.ExternalId,
                Merchant = row.Merchant ?? batchMerchant,
                SourceFileName = row.SourceFileName,
                SuggestedCategoryId = suggestion.CategoryId,
                SuggestedNotes = suggestion.Notes ?? row.Description,
                SuggestionSource = suggestion.Source
            });
        }

        await ApplyImportFilteringAsync(batch, items, profile, accountId, ledgerEntityId, autoAccept, ct);

        foreach (var item in items)
            batch.Items.Add(item);

        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        await MarkDuplicatesAsync(batch, accountId, fileSha256, fileSizeBytes, ct);

        if (autoAccept)
        {
            foreach (var item in batch.Items.Where(i => i.Status == ImportItemStatus.Pending))
            {
                if (item.SuggestedCategoryId is null)
                {
                    item.Status = ImportItemStatus.Skipped;
                    item.SkipReason = ImportSkipReasons.NoCategory;
                    continue;
                }

                var result = await AcceptItemAsync(new AcceptImportItemRequest(
                    item.Id,
                    item.Date,
                    item.Amount,
                    item.SuggestedCategoryId.Value,
                    ledgerEntityId,
                    accountId,
                    item.SuggestedNotes ?? item.Description), ct);

                if (result.Status == ImportAcceptStatus.SkippedDuplicate && item.SkipReason is null)
                    item.SkipReason = ImportSkipReasons.DuplicateTransaction;
            }

            await _db.SaveChangesAsync(ct);
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

    public async Task<AcceptItemResult> AcceptItemAsync(AcceptImportItemRequest request, CancellationToken ct = default)
    {
        var item = await _db.ImportItems
            .Include(i => i.ImportBatch)
            .FirstOrDefaultAsync(i => i.Id == request.ItemId, ct);

        if (item is null || item.Status != ImportItemStatus.Pending)
        {
            return new AcceptItemResult(
                ImportAcceptStatus.InvalidState,
                "This import row is no longer pending and was not saved.",
                null);
        }

        var duplicateReason = await FindDuplicateReasonAsync(
            ImportDuplicateMatcher.FromItem(item),
            request.AccountId,
            item.ImportBatch?.FileSha256,
            item.ImportBatch?.FileSizeBytes,
            ct);

        if (duplicateReason is not null)
        {
            item.Status = ImportItemStatus.Skipped;
            item.SkipReason = duplicateReason;
            item.SuggestedSkipReason = null;
            await _db.SaveChangesAsync(ct);
            await CompleteBatchIfDoneAsync(item.ImportBatchId, ct);
            return new AcceptItemResult(
                ImportAcceptStatus.SkippedDuplicate,
                ImportSkipReasons.Describe(duplicateReason),
                null);
        }

        var transaction = new Transaction
        {
            Date = request.Date,
            Amount = request.Amount,
            CategoryId = request.CategoryId,
            LedgerEntityId = request.LedgerEntityId,
            AccountId = request.AccountId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ExternalId = item.ExternalId,
            ImportBatchId = item.ImportBatchId,
            Merchant = item.Merchant ?? item.ImportBatch?.Merchant,
            SourceFileName = item.SourceFileName ?? item.ImportBatch?.SourcePath
        };

        _db.Transactions.Add(transaction);
        item.Status = ImportItemStatus.Accepted;
        item.SuggestedSkipReason = null;
        await _db.SaveChangesAsync(ct);

        item.ResultingTransactionId = transaction.Id;
        await _db.SaveChangesAsync(ct);
        await CompleteBatchIfDoneAsync(item.ImportBatchId, ct);
        return new AcceptItemResult(ImportAcceptStatus.Accepted, null, transaction);
    }

    public async Task SkipItemAsync(int itemId, CancellationToken ct = default)
    {
        var item = await _db.ImportItems.FindAsync([itemId], ct);
        if (item is null) return;

        item.Status = ImportItemStatus.Skipped;
        item.SkipReason = item.SuggestedSkipReason ?? ImportSkipReasons.UserSkipped;
        item.SuggestedSkipReason = null;
        await _db.SaveChangesAsync(ct);
        await CompleteBatchIfDoneAsync(item.ImportBatchId, ct);
    }

    public async Task<DeleteBatchResult> DeleteIncompleteBatchAsync(string batchId, CancellationToken ct = default)
    {
        var batch = await _db.ImportBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);

        if (batch is null)
            return new DeleteBatchResult(DeleteBatchStatus.NotFound, 0);

        if (batch.Status == ImportBatchStatus.Completed)
            return new DeleteBatchResult(DeleteBatchStatus.AlreadyCompleted, 0);

        var kept = batch.Items.Count(i => i.Status == ImportItemStatus.Accepted);
        var txnIds = batch.Items
            .Where(i => i.ResultingTransactionId != null)
            .Select(i => i.ResultingTransactionId!.Value)
            .ToList();

        if (txnIds.Count > 0)
        {
            var txns = await _db.Transactions.Where(t => txnIds.Contains(t.Id)).ToListAsync(ct);
            foreach (var txn in txns)
                txn.ImportBatchId = null;
        }

        _db.ImportBatches.Remove(batch);
        await _db.SaveChangesAsync(ct);
        return new DeleteBatchResult(DeleteBatchStatus.Deleted, kept);
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

    private async Task ApplyImportFilteringAsync(
        ImportBatch batch,
        List<ImportItem> items,
        ImportProfile? profile,
        int accountId,
        int ledgerEntityId,
        bool autoAccept,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return;

        var rules = profile?.Rules.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToList() ?? [];
        var skippedIndices = new HashSet<int>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var ruleMatch = _ruleMatcher.Match(item.Description, rules);
            if (ruleMatch is not null)
            {
                ApplySkipDecision(item, ruleMatch.SkipKind, autoAccept);
                if (item.Status == ImportItemStatus.Skipped)
                    skippedIndices.Add(i);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (skippedIndices.Contains(i) || items[i].Status != ImportItemStatus.Pending)
                continue;

            var item = items[i];
            var batchPair = _transferMatcher.FindBatchPairSkipReason(item, i, items, skippedIndices);
            if (batchPair is not null)
            {
                ApplySkipDecision(item, batchPair, autoAccept);
                if (item.Status == ImportItemStatus.Skipped)
                    skippedIndices.Add(i);
                continue;
            }

            var existingPair = await _transferMatcher.FindExistingPairSkipReasonAsync(
                ledgerEntityId, accountId, item.Date, item.Amount, ct);
            if (existingPair is not null)
            {
                ApplySkipDecision(item, existingPair, autoAccept);
                if (item.Status == ImportItemStatus.Skipped)
                    skippedIndices.Add(i);
            }
        }

        if (profile?.UseLlmForUnmatched == true && _rowClassifier.IsEnabled)
        {
            var unmatched = items
                .Select((item, index) => (item, index))
                .Where(t => t.item.Status == ImportItemStatus.Pending && t.item.SuggestedSkipReason is null)
                .ToList();

            if (unmatched.Count > 0)
            {
                try
                {
                    var requests = unmatched
                        .Select(t => new ImportClassificationRequest(t.index, t.item.Description, t.item.Amount))
                        .ToList();

                    var classifications = await _rowClassifier.ClassifyBatchAsync(requests, ct);
                    foreach (var (item, index) in unmatched)
                    {
                        if (!classifications.TryGetValue(index, out var classification))
                            continue;

                        if (classification.Action == "skip")
                        {
                            ApplySkipDecision(item, classification.SkipKind ?? ImportSkipReasons.LlmSuggestedSkip, autoAccept);
                            batch.LlmClassifiedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LLM import classification failed; continuing without AI skip suggestions.");
                }
            }
        }
    }

    private void PopulateLlmSnapshot(ImportBatch batch, ImportProfile? profile)
    {
        batch.LlmConfiguredAtImport = _settings.Enabled;
        batch.LlmCategorizationAvailable = _llmClient.IsEnabled;
        batch.LlmClassificationAvailable = profile?.UseLlmForUnmatched == true && _rowClassifier.IsEnabled;

        var notes = new List<string>();
        if (_settings.Enabled && !_llmClient.IsEnabled)
        {
            var reason = _settings.DescribeCategorizationBlocker();
            if (reason is not null)
                notes.Add($"Categorization unavailable: {reason}");
        }

        if (_settings.Enabled && profile?.UseLlmForUnmatched == true && !_rowClassifier.IsEnabled)
        {
            var reason = _settings.DescribeImportClassificationBlocker();
            if (reason is not null)
                notes.Add($"Skip classification unavailable: {reason}");
        }

        batch.LlmAvailabilityNotes = notes.Count > 0 ? string.Join(" ", notes) : null;
    }

    private static void ApplySkipDecision(ImportItem item, string skipKind, bool autoAccept)
    {
        if (autoAccept)
        {
            item.Status = ImportItemStatus.Skipped;
            item.SkipReason = skipKind;
            item.SuggestedSkipReason = null;
        }
        else
        {
            item.SuggestedSkipReason = skipKind;
        }
    }

    private async Task MarkDuplicatesAsync(
        ImportBatch batch,
        int accountId,
        string fileSha256,
        long fileSizeBytes,
        CancellationToken ct)
    {
        if (!batch.Items.Any())
            return;

        var minDate = batch.Items.Min(i => i.Date);
        var maxDate = batch.Items.Max(i => i.Date);

        var existingTransactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId && t.Date >= minDate && t.Date <= maxDate)
            .ToListAsync(ct);

        var priorFingerprints = await GetPriorImportFingerprintsAsync(fileSha256, fileSizeBytes, accountId, batch.Id, ct);

        var seenInBatch = new List<ImportDuplicateMatcher.TransactionFingerprint>();

        foreach (var item in batch.Items.OrderBy(i => i.Id))
        {
            if (item.Status == ImportItemStatus.Skipped)
                continue;

            var fingerprint = ImportDuplicateMatcher.FromItem(item);

            if (seenInBatch.Any(p => ImportDuplicateMatcher.SameRow(p, fingerprint)))
            {
                item.Status = ImportItemStatus.Skipped;
                item.SkipReason = ImportSkipReasons.DuplicateTransaction;
                item.SuggestedSkipReason = null;
                continue;
            }

            var reason = ResolveDuplicateReason(fingerprint, existingTransactions, priorFingerprints);
            if (reason is not null)
            {
                item.Status = ImportItemStatus.Skipped;
                item.SkipReason = reason;
                item.SuggestedSkipReason = null;
                continue;
            }

            seenInBatch.Add(fingerprint);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<string?> FindDuplicateReasonAsync(
        ImportDuplicateMatcher.TransactionFingerprint fingerprint,
        int? accountId,
        string? fileSha256,
        long? fileSizeBytes,
        CancellationToken ct)
    {
        if (accountId is null)
            return null;

        var existing = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId && t.Date == fingerprint.Date)
            .ToListAsync(ct);

        foreach (var transaction in existing)
        {
            if (ImportDuplicateMatcher.MatchesTransaction(fingerprint, transaction))
            {
                return !string.IsNullOrWhiteSpace(fingerprint.ExternalId)
                    ? ImportSkipReasons.DuplicateExternalId
                    : ImportSkipReasons.DuplicateTransaction;
            }
        }

        if (fileSha256 is null || fileSizeBytes is null)
            return null;

        var priorFingerprints = await GetPriorImportFingerprintsAsync(
            fileSha256, fileSizeBytes.Value, accountId.Value, excludeBatchId: null, ct);

        return priorFingerprints.Any(p => ImportDuplicateMatcher.SameRow(fingerprint, p))
            ? ImportSkipReasons.DuplicatePriorImport
            : null;
    }

    private async Task<List<ImportDuplicateMatcher.TransactionFingerprint>> GetPriorImportFingerprintsAsync(
        string fileSha256,
        long fileSizeBytes,
        int accountId,
        string? excludeBatchId,
        CancellationToken ct)
    {
        var priorBatchQuery = _db.ImportBatches
            .AsNoTracking()
            .Where(b => b.FileSha256 == fileSha256
                && b.FileSizeBytes == fileSizeBytes
                && b.AccountId == accountId
                && b.Status == ImportBatchStatus.Completed);

        if (excludeBatchId is not null)
            priorBatchQuery = priorBatchQuery.Where(b => b.Id != excludeBatchId);

        var priorBatchIds = await priorBatchQuery.Select(b => b.Id).ToListAsync(ct);
        if (priorBatchIds.Count == 0)
            return [];

        return await _db.ImportItems
            .AsNoTracking()
            .Where(i => priorBatchIds.Contains(i.ImportBatchId) && i.Status == ImportItemStatus.Accepted)
            .Select(i => new ImportDuplicateMatcher.TransactionFingerprint(i.Date, i.Amount, i.Description, i.ExternalId))
            .ToListAsync(ct);
    }

    private static string? ResolveDuplicateReason(
        ImportDuplicateMatcher.TransactionFingerprint fingerprint,
        IReadOnlyList<Transaction> existingTransactions,
        IReadOnlyList<ImportDuplicateMatcher.TransactionFingerprint> priorFingerprints)
    {
        foreach (var transaction in existingTransactions)
        {
            if (ImportDuplicateMatcher.MatchesTransaction(fingerprint, transaction))
            {
                return !string.IsNullOrWhiteSpace(fingerprint.ExternalId)
                    ? ImportSkipReasons.DuplicateExternalId
                    : ImportSkipReasons.DuplicateTransaction;
            }
        }

        if (priorFingerprints.Any(p => ImportDuplicateMatcher.SameRow(fingerprint, p)))
            return ImportSkipReasons.DuplicatePriorImport;

        return null;
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
        var desc1Col = FindColumn(headers, "description1");
        var desc2Col = FindColumn(headers, "description2");
        var idCol = FindColumn(headers, "id", "transactionid", "referencenumber", "referenceno");

        var rows = new List<CsvImportRow>();
        while (csv.Read())
        {
            var date = ParseDate(csv, dateCol);
            var amount = ParseAmount(csv, headers, amountCol);
            var description = BuildDescription(csv, descCol, desc1Col, desc2Col);
            var externalId = idCol >= 0 ? csv.GetField(idCol)?.Trim() : null;

            if (date is null || amount is null || string.IsNullOrWhiteSpace(description))
                continue;

            rows.Add(new CsvImportRow(date.Value, amount.Value, description, externalId));
        }

        return rows;
    }

    private static string BuildDescription(CsvReader csv, int descCol, int desc1Col, int desc2Col)
    {
        if (desc1Col >= 0 || desc2Col >= 0)
        {
            var parts = new List<string>();
            if (desc1Col >= 0)
            {
                var d1 = csv.GetField(desc1Col)?.Trim();
                if (!string.IsNullOrWhiteSpace(d1))
                    parts.Add(d1);
            }

            if (desc2Col >= 0)
            {
                var d2 = csv.GetField(desc2Col)?.Trim();
                if (!string.IsNullOrWhiteSpace(d2))
                    parts.Add(d2);
            }

            if (parts.Count > 0)
                return string.Join(" | ", parts);
        }

        return descCol >= 0 ? csv.GetField(descCol)?.Trim() ?? "" : "";
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

    private async Task<CategorySuggestion> ResolveCategorySuggestionAsync(
        CsvImportRow row,
        IReadOnlyList<Category> categories,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(row.SuggestedCategoryName))
        {
            var receiptCategory = categories.FirstOrDefault(c =>
                c.Name.Equals(row.SuggestedCategoryName, StringComparison.OrdinalIgnoreCase));

            if (receiptCategory is not null)
            {
                return new CategorySuggestion(
                    receiptCategory.Id,
                    row.Description,
                    "llm");
            }
        }

        return await _categorizer.SuggestAsync(row.Description, row.Amount, categories, ct);
    }
}
