using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using HomeLedger.Core.Entities;
using HomeLedger.Core.Export;
using HomeLedger.Core.Utilities;
using HomeLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Infrastructure.Export;

public record HomeLedgerImportResult(
    int EntitiesImported,
    int AccountsImported,
    int CategoryGroupsImported,
    int CategoriesImported,
    int BudgetsImported,
    int TransactionsImported,
    int TransactionsSkipped,
    IReadOnlyList<LedgerImportSkippedRow> SkippedTransactions);

public record LedgerImportSkippedRow(DateOnly Date, decimal Amount, string? Notes, string Reason);

public interface IHomeLedgerExportService
{
    Task<byte[]> ExportCsvAsync(CancellationToken ct = default);
    bool IsLedgerExport(IReadOnlyList<string> headers);
    Task<HomeLedgerImportResult> ImportCsvAsync(Stream csvStream, CancellationToken ct = default);
    string[] ReadCsvHeaders(Stream csvStream);
}

public class HomeLedgerExportService : IHomeLedgerExportService
{
    private readonly HomeLedgerDbContext _db;

    public HomeLedgerExportService(HomeLedgerDbContext db) => _db = db;

    public string[] ReadCsvHeaders(Stream csvStream)
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

        return csv.HeaderRecord ?? [];
    }

    public async Task<byte[]> ExportCsvAsync(CancellationToken ct = default)
    {
        var entities = await _db.Entities.AsNoTracking().OrderBy(e => e.Name).ToListAsync(ct);
        var accounts = await _db.Accounts.AsNoTracking().Include(a => a.LedgerEntity).OrderBy(a => a.Name).ToListAsync(ct);
        var groups = await _db.CategoryGroups.AsNoTracking().Include(g => g.LedgerEntity).OrderBy(g => g.SortOrder).ThenBy(g => g.Name).ToListAsync(ct);
        var categories = await _db.Categories.AsNoTracking()
            .Include(c => c.CategoryGroup)
            .Include(c => c.LedgerEntity)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var budgets = await _db.BudgetLimits.AsNoTracking()
            .Include(b => b.Category).ThenInclude(c => c.CategoryGroup)
            .Include(b => b.LedgerEntity)
            .OrderBy(b => b.Id).ToListAsync(ct);
        var transactions = await _db.Transactions.AsNoTracking()
            .Include(t => t.Category).ThenInclude(c => c.CategoryGroup)
            .Include(t => t.LedgerEntity)
            .Include(t => t.Account)
            .OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync(ct);

        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim
        });

        foreach (var header in HomeLedgerExportFormat.Headers)
            csv.WriteField(header);
        await csv.NextRecordAsync();

        WriteRow(csv, HomeLedgerExportFormat.RecordMeta, notes: HomeLedgerExportFormat.Version,
            createdAt: DateTime.UtcNow.ToString("O"));

        foreach (var entity in entities)
        {
            WriteRow(csv, HomeLedgerExportFormat.RecordEntity,
                entityName: entity.Name,
                color: entity.Color,
                isActive: entity.IsActive,
                createdAt: entity.CreatedAt.ToString("O"));
        }

        foreach (var account in accounts)
        {
            WriteRow(csv, HomeLedgerExportFormat.RecordAccount,
                entityName: account.LedgerEntity.Name,
                accountName: account.Name,
                institution: account.Institution,
                accountNumberLast4: account.AccountNumberLast4,
                isActive: account.IsActive);
        }

        foreach (var group in groups)
        {
            WriteRow(csv, HomeLedgerExportFormat.RecordCategoryGroup,
                entityName: ScopeName(group.LedgerEntity?.Name),
                categoryGroupName: group.Name,
                isIncome: group.IsIncome,
                sortOrder: group.SortOrder,
                isActive: group.IsActive);
        }

        foreach (var category in categories)
        {
            WriteRow(csv, HomeLedgerExportFormat.RecordCategory,
                entityName: ScopeName(category.LedgerEntity?.Name),
                categoryGroupName: category.CategoryGroup.Name,
                categoryName: category.Name,
                isIncome: category.IsIncome,
                sortOrder: category.SortOrder,
                isActive: category.IsActive);
        }

        foreach (var budget in budgets)
        {
            WriteRow(csv, HomeLedgerExportFormat.RecordBudget,
                entityName: ScopeName(budget.LedgerEntity?.Name),
                categoryGroupName: budget.Category.CategoryGroup.Name,
                categoryName: budget.Category.Name,
                limitAmount: budget.LimitAmount,
                warningThresholdPercent: budget.WarningThresholdPercent,
                period: budget.Period.ToString(),
                customStartDate: FormatDate(budget.CustomStartDate),
                customEndDate: FormatDate(budget.CustomEndDate),
                isActive: budget.IsActive);
        }

        foreach (var transaction in transactions)
        {
            WriteRow(csv, HomeLedgerExportFormat.RecordTransaction,
                entityName: transaction.LedgerEntity.Name,
                accountName: transaction.Account?.Name,
                categoryGroupName: transaction.Category.CategoryGroup.Name,
                categoryName: transaction.Category.Name,
                date: FormatDate(transaction.Date),
                amount: transaction.Amount,
                notes: transaction.Notes,
                externalId: transaction.ExternalId,
                importBatchId: transaction.ImportBatchId,
                createdAt: transaction.CreatedAt.ToString("O"),
                updatedAt: transaction.UpdatedAt?.ToString("O"));
        }

        await csv.FlushAsync();
        await writer.FlushAsync(ct);
        return stream.ToArray();
    }

    public bool IsLedgerExport(IReadOnlyList<string> headers)
    {
        if (headers.Count == 0)
            return false;

        var normalized = headers
            .Select(h => h.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", ""))
            .ToList();

        return normalized.Contains("recordtype")
            && normalized.Contains("entityname")
            && normalized.Contains("categorygroupname");
    }

    public async Task<HomeLedgerImportResult> ImportCsvAsync(Stream csvStream, CancellationToken ct = default)
    {
        var rows = ParseRows(csvStream);
        ValidateMeta(rows);

        var entityMap = new Dictionary<string, LedgerEntity>(StringComparer.OrdinalIgnoreCase);
        var accountMap = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
        var groupMap = new Dictionary<string, CategoryGroup>(StringComparer.OrdinalIgnoreCase);
        var categoryMap = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

        var entitiesImported = 0;
        var accountsImported = 0;
        var groupsImported = 0;
        var categoriesImported = 0;
        var budgetsImported = 0;
        var transactionsImported = 0;
        var skipped = new List<LedgerImportSkippedRow>();

        foreach (var row in rows.Where(r => r.RecordType == HomeLedgerExportFormat.RecordEntity))
        {
            var name = Require(row.EntityName, "entity name");
            var entity = await _db.Entities.FirstOrDefaultAsync(e => e.Name == name, ct)
                ?? new LedgerEntity { Name = name };

            entity.Color = NullIfEmpty(row.Color);
            entity.IsActive = ParseBool(row.IsActive, true);
            if (DateTime.TryParse(row.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
                entity.CreatedAt = created;

            if (entity.Id == 0)
            {
                _db.Entities.Add(entity);
                entitiesImported++;
            }

            entityMap[name] = entity;
        }

        await _db.SaveChangesAsync(ct);

        foreach (var row in rows.Where(r => r.RecordType == HomeLedgerExportFormat.RecordAccount))
        {
            var entityName = Require(row.EntityName, "entity name");
            var accountName = Require(row.AccountName, "account name");
            if (!entityMap.TryGetValue(entityName, out var entity))
                throw new InvalidOperationException($"Account '{accountName}' references unknown entity '{entityName}'.");

            var account = await _db.Accounts.FirstOrDefaultAsync(
                a => a.Name == accountName && a.LedgerEntityId == entity.Id, ct)
                ?? new Account { Name = accountName, LedgerEntityId = entity.Id };

            account.Institution = NullIfEmpty(row.Institution);
            account.AccountNumberLast4 = NullIfEmpty(row.AccountNumberLast4);
            account.IsActive = ParseBool(row.IsActive, true);

            if (account.Id == 0)
            {
                _db.Accounts.Add(account);
                accountsImported++;
            }

            accountMap[AccountKey(entityName, accountName)] = account;
        }

        await _db.SaveChangesAsync(ct);

        foreach (var row in rows.Where(r => r.RecordType == HomeLedgerExportFormat.RecordCategoryGroup))
        {
            var entityId = await ResolveScopeEntityIdAsync(row.EntityName, entityMap, ct);
            var groupName = Require(row.CategoryGroupName, "category group name");
            var key = GroupKey(row.EntityName, groupName);

            var group = await _db.CategoryGroups.FirstOrDefaultAsync(
                g => g.Name == groupName && g.LedgerEntityId == entityId, ct)
                ?? new CategoryGroup { Name = groupName, LedgerEntityId = entityId };

            group.IsIncome = ParseBool(row.IsIncome, false);
            group.SortOrder = ParseInt(row.SortOrder, group.SortOrder);
            group.IsActive = ParseBool(row.IsActive, true);

            if (group.Id == 0)
            {
                _db.CategoryGroups.Add(group);
                groupsImported++;
            }

            groupMap[key] = group;
        }

        await _db.SaveChangesAsync(ct);

        foreach (var row in rows.Where(r => r.RecordType == HomeLedgerExportFormat.RecordCategory))
        {
            var entityId = await ResolveScopeEntityIdAsync(row.EntityName, entityMap, ct);
            var groupName = Require(row.CategoryGroupName, "category group name");
            var categoryName = Require(row.CategoryName, "category name");
            var groupKey = GroupKey(row.EntityName, groupName);

            if (!groupMap.TryGetValue(groupKey, out var group))
            {
                group = await _db.CategoryGroups.FirstOrDefaultAsync(
                    g => g.Name == groupName && g.LedgerEntityId == entityId, ct)
                    ?? throw new InvalidOperationException($"Category '{categoryName}' references unknown group '{groupName}'.");
                groupMap[groupKey] = group;
            }

            var category = await _db.Categories.FirstOrDefaultAsync(
                c => c.Name == categoryName && c.LedgerEntityId == entityId, ct)
                ?? new Category
                {
                    Name = categoryName,
                    CategoryGroupId = group.Id,
                    LedgerEntityId = entityId
                };

            category.CategoryGroupId = group.Id;
            category.IsIncome = ParseBool(row.IsIncome, category.IsIncome);
            category.SortOrder = ParseInt(row.SortOrder, category.SortOrder);
            category.IsActive = ParseBool(row.IsActive, true);

            if (category.Id == 0)
            {
                _db.Categories.Add(category);
                categoriesImported++;
            }

            categoryMap[CategoryKey(row.EntityName, categoryName)] = category;
        }

        await _db.SaveChangesAsync(ct);

        foreach (var row in rows.Where(r => r.RecordType == HomeLedgerExportFormat.RecordBudget))
        {
            var entityId = await ResolveScopeEntityIdAsync(row.EntityName, entityMap, ct);
            var categoryName = Require(row.CategoryName, "category name");
            var categoryKey = CategoryKey(row.EntityName, categoryName);

            if (!categoryMap.TryGetValue(categoryKey, out var category))
            {
                category = await _db.Categories.FirstOrDefaultAsync(
                    c => c.Name == categoryName && c.LedgerEntityId == entityId, ct)
                    ?? throw new InvalidOperationException($"Budget references unknown category '{categoryName}'.");
                categoryMap[categoryKey] = category;
            }

            var period = Enum.TryParse<BudgetPeriod>(row.Period, true, out var parsedPeriod)
                ? parsedPeriod
                : BudgetPeriod.Monthly;
            var customStart = ParseDate(row.CustomStartDate);
            var customEnd = ParseDate(row.CustomEndDate);
            var limitAmount = ParseDecimal(row.LimitAmount)
                ?? throw new InvalidOperationException($"Budget for '{categoryName}' is missing LimitAmount.");

            var budget = await _db.BudgetLimits.FirstOrDefaultAsync(b =>
                b.CategoryId == category.Id
                && b.LedgerEntityId == entityId
                && b.Period == period
                && b.CustomStartDate == customStart
                && b.CustomEndDate == customEnd, ct)
                ?? new BudgetLimit
                {
                    CategoryId = category.Id,
                    LedgerEntityId = entityId,
                    Period = period,
                    CustomStartDate = customStart,
                    CustomEndDate = customEnd
                };

            budget.LimitAmount = limitAmount;
            budget.WarningThresholdPercent = ParseDecimal(row.WarningThresholdPercent) ?? budget.WarningThresholdPercent;
            budget.IsActive = ParseBool(row.IsActive, true);

            if (budget.Id == 0)
            {
                _db.BudgetLimits.Add(budget);
                budgetsImported++;
            }
        }

        await _db.SaveChangesAsync(ct);

        var existingTransactions = await _db.Transactions
            .AsNoTracking()
            .Select(t => new TransactionSnapshot(t.AccountId, t.ExternalId, t.Date, t.Amount, t.Notes))
            .ToListAsync(ct);

        foreach (var row in rows.Where(r => r.RecordType == HomeLedgerExportFormat.RecordTransaction))
        {
            var entityName = Require(row.EntityName, "entity name");
            var categoryName = Require(row.CategoryName, "category name");
            var date = ParseDate(row.Date)
                ?? throw new InvalidOperationException("Transaction row is missing a valid Date.");
            var amount = ParseDecimal(row.Amount)
                ?? throw new InvalidOperationException("Transaction row is missing a valid Amount.");

            if (!entityMap.TryGetValue(entityName, out var entity))
                throw new InvalidOperationException($"Transaction references unknown entity '{entityName}'.");

            var categoryKey = CategoryKey(row.EntityName, categoryName);
            if (!categoryMap.TryGetValue(categoryKey, out var category))
            {
                category = await _db.Categories.FirstOrDefaultAsync(
                    c => c.Name == categoryName && c.LedgerEntityId == entity.Id, ct)
                    ?? throw new InvalidOperationException($"Transaction references unknown category '{categoryName}'.");
            }

            int? accountId = null;
            if (!string.IsNullOrWhiteSpace(row.AccountName))
            {
                var accountKey = AccountKey(entityName, row.AccountName);
                if (!accountMap.TryGetValue(accountKey, out var account))
                {
                    account = await _db.Accounts.FirstOrDefaultAsync(
                        a => a.Name == row.AccountName && a.LedgerEntityId == entity.Id, ct)
                        ?? throw new InvalidOperationException($"Transaction references unknown account '{row.AccountName}'.");
                }

                accountId = account.Id;
            }

            var notes = NullIfEmpty(row.Notes);
            var externalId = NullIfEmpty(row.ExternalId);
            var skipReason = FindDuplicateReason(existingTransactions, accountId, externalId, date, amount, notes);
            if (skipReason is not null)
            {
                skipped.Add(new LedgerImportSkippedRow(date, amount, notes, skipReason));
                continue;
            }

            var transaction = new Transaction
            {
                Date = date,
                Amount = amount,
                CategoryId = category.Id,
                LedgerEntityId = entity.Id,
                AccountId = accountId,
                Notes = notes,
                ExternalId = externalId,
                ImportBatchId = NullIfEmpty(row.ImportBatchId)
            };

            if (DateTime.TryParse(row.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
                transaction.CreatedAt = created;
            if (DateTime.TryParse(row.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated))
                transaction.UpdatedAt = updated;

            _db.Transactions.Add(transaction);
            transactionsImported++;
            existingTransactions.Add(new TransactionSnapshot(accountId, externalId, date, amount, notes));
        }

        await _db.SaveChangesAsync(ct);

        return new HomeLedgerImportResult(
            entitiesImported,
            accountsImported,
            groupsImported,
            categoriesImported,
            budgetsImported,
            transactionsImported,
            skipped.Count,
            skipped);
    }

    private static List<LedgerExportRow> ParseRows(Stream csvStream)
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
        var index = BuildHeaderIndex(headers);
        var rows = new List<LedgerExportRow>();

        while (csv.Read())
        {
            rows.Add(new LedgerExportRow
            {
                RecordType = GetField(csv, index, "RecordType")?.Trim().ToLowerInvariant() ?? "",
                EntityName = GetField(csv, index, "EntityName"),
                AccountName = GetField(csv, index, "AccountName"),
                CategoryGroupName = GetField(csv, index, "CategoryGroupName"),
                CategoryName = GetField(csv, index, "CategoryName"),
                Date = GetField(csv, index, "Date"),
                Amount = GetField(csv, index, "Amount"),
                Notes = GetField(csv, index, "Notes"),
                ExternalId = GetField(csv, index, "ExternalId"),
                ImportBatchId = GetField(csv, index, "ImportBatchId"),
                Institution = GetField(csv, index, "Institution"),
                AccountNumberLast4 = GetField(csv, index, "AccountNumberLast4"),
                Color = GetField(csv, index, "Color"),
                IsActive = GetField(csv, index, "IsActive"),
                IsIncome = GetField(csv, index, "IsIncome"),
                SortOrder = GetField(csv, index, "SortOrder"),
                LimitAmount = GetField(csv, index, "LimitAmount"),
                WarningThresholdPercent = GetField(csv, index, "WarningThresholdPercent"),
                Period = GetField(csv, index, "Period"),
                CustomStartDate = GetField(csv, index, "CustomStartDate"),
                CustomEndDate = GetField(csv, index, "CustomEndDate"),
                CreatedAt = GetField(csv, index, "CreatedAt"),
                UpdatedAt = GetField(csv, index, "UpdatedAt")
            });
        }

        return rows;
    }

    private static void ValidateMeta(IReadOnlyList<LedgerExportRow> rows)
    {
        var meta = rows.FirstOrDefault(r => r.RecordType == HomeLedgerExportFormat.RecordMeta);
        if (meta is null || !string.Equals(meta.Notes, HomeLedgerExportFormat.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported or missing ledger export format. Expected meta row with version '{HomeLedgerExportFormat.Version}'.");
    }

    private static string? FindDuplicateReason(
        IEnumerable<TransactionSnapshot> existing,
        int? accountId,
        string? externalId,
        DateOnly date,
        decimal amount,
        string? notes)
    {
        foreach (var transaction in existing)
        {
            if (!string.IsNullOrWhiteSpace(externalId)
                && accountId is not null
                && string.Equals(transaction.ExternalId, externalId, StringComparison.OrdinalIgnoreCase)
                && transaction.AccountId == accountId)
            {
                return "Already imported (matching external ID)";
            }
        }

        var normalizedNotes = Normalize(notes);
        foreach (var transaction in existing)
        {
            if (transaction.AccountId != accountId
                || transaction.Date != date
                || transaction.Amount != amount)
            {
                continue;
            }

            var existingNotes = Normalize(transaction.Notes);
            if (existingNotes == normalizedNotes
                || (!string.IsNullOrEmpty(normalizedNotes) && existingNotes.Contains(normalizedNotes, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(existingNotes) && normalizedNotes.Contains(existingNotes, StringComparison.Ordinal)))
            {
                return "Already imported (matching date, amount, and notes)";
            }
        }

        return null;
    }

    private readonly record struct TransactionSnapshot(
        int? AccountId,
        string? ExternalId,
        DateOnly Date,
        decimal Amount,
        string? Notes);

    private async Task<int?> ResolveScopeEntityIdAsync(
        string? entityName,
        IReadOnlyDictionary<string, LedgerEntity> entityMap,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            return null;

        if (entityMap.TryGetValue(entityName, out var entity))
            return entity.Id;

        var existing = await _db.Entities.AsNoTracking().FirstOrDefaultAsync(e => e.Name == entityName, ct);
        return existing?.Id;
    }

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> headers)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
            index.TryAdd(key, i);
        }

        return index;
    }

    private static string? GetField(CsvReader csv, IReadOnlyDictionary<string, int> index, string name)
    {
        var key = name.ToLowerInvariant().Replace(" ", "").Replace("_", "");
        return index.TryGetValue(key, out var column) ? csv.GetField(column) : null;
    }

    private static void WriteRow(
        CsvWriter csv,
        string recordType,
        string? entityName = null,
        string? accountName = null,
        string? categoryGroupName = null,
        string? categoryName = null,
        string? date = null,
        decimal? amount = null,
        string? notes = null,
        string? externalId = null,
        string? importBatchId = null,
        string? institution = null,
        string? accountNumberLast4 = null,
        string? color = null,
        bool? isActive = null,
        bool? isIncome = null,
        int? sortOrder = null,
        decimal? limitAmount = null,
        decimal? warningThresholdPercent = null,
        string? period = null,
        string? customStartDate = null,
        string? customEndDate = null,
        string? createdAt = null,
        string? updatedAt = null)
    {
        csv.WriteField(recordType);
        csv.WriteField(entityName);
        csv.WriteField(accountName);
        csv.WriteField(categoryGroupName);
        csv.WriteField(categoryName);
        csv.WriteField(date);
        csv.WriteField(amount?.ToString(CultureInfo.InvariantCulture));
        csv.WriteField(notes);
        csv.WriteField(externalId);
        csv.WriteField(importBatchId);
        csv.WriteField(institution);
        csv.WriteField(accountNumberLast4);
        csv.WriteField(color);
        csv.WriteField(isActive?.ToString());
        csv.WriteField(isIncome?.ToString());
        csv.WriteField(sortOrder?.ToString(CultureInfo.InvariantCulture));
        csv.WriteField(limitAmount?.ToString(CultureInfo.InvariantCulture));
        csv.WriteField(warningThresholdPercent?.ToString(CultureInfo.InvariantCulture));
        csv.WriteField(period);
        csv.WriteField(customStartDate);
        csv.WriteField(customEndDate);
        csv.WriteField(createdAt);
        csv.WriteField(updatedAt);
        csv.NextRecord();
    }

    private static string ScopeName(string? entityName) => entityName ?? "";

    private static string AccountKey(string entityName, string accountName) => $"{entityName}|{accountName}";

    private static string GroupKey(string? entityName, string groupName) => $"{entityName ?? ""}|{groupName}";

    private static string CategoryKey(string? entityName, string categoryName) => $"{entityName ?? ""}|{categoryName}";

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Require(string? value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"Missing {label} in ledger export.") : value.Trim();

    private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();

    private static bool ParseBool(string? value, bool defaultValue) =>
        bool.TryParse(value, out var result) ? result : defaultValue;

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (HomeLedgerFormats.TryParseDate(value, out var date))
            return date;

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date : null;
    }

    private static string? FormatDate(DateOnly? date) => date is null ? null : HomeLedgerFormats.FormatDate(date.Value);

    private sealed class LedgerExportRow
    {
        public string RecordType { get; set; } = "";
        public string? EntityName { get; set; }
        public string? AccountName { get; set; }
        public string? CategoryGroupName { get; set; }
        public string? CategoryName { get; set; }
        public string? Date { get; set; }
        public string? Amount { get; set; }
        public string? Notes { get; set; }
        public string? ExternalId { get; set; }
        public string? ImportBatchId { get; set; }
        public string? Institution { get; set; }
        public string? AccountNumberLast4 { get; set; }
        public string? Color { get; set; }
        public string? IsActive { get; set; }
        public string? IsIncome { get; set; }
        public string? SortOrder { get; set; }
        public string? LimitAmount { get; set; }
        public string? WarningThresholdPercent { get; set; }
        public string? Period { get; set; }
        public string? CustomStartDate { get; set; }
        public string? CustomEndDate { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
    }
}
