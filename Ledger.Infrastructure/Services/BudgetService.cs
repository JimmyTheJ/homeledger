using Ledger.Core.Entities;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Services;

public record PeriodRange(DateOnly Start, DateOnly End);

public record BudgetStatus(
    BudgetLimit Limit,
    decimal Spent,
    decimal PercentUsed,
    bool IsOverBudget,
    bool IsWarning);

public interface IBudgetService
{
    PeriodRange GetPeriodRange(BudgetLimit limit, DateOnly referenceDate);
    Task<IReadOnlyList<BudgetStatus>> GetStatusesAsync(
        DateOnly referenceDate,
        int? ledgerEntityId = null,
        CancellationToken ct = default);
}

public class BudgetService : IBudgetService
{
    private readonly LedgerDbContext _db;

    public BudgetService(LedgerDbContext db) => _db = db;

    public PeriodRange GetPeriodRange(BudgetLimit limit, DateOnly referenceDate) =>
        limit.Period switch
        {
            BudgetPeriod.Weekly => WeekContaining(referenceDate),
            BudgetPeriod.Monthly => MonthContaining(referenceDate),
            BudgetPeriod.Quarterly => QuarterContaining(referenceDate),
            BudgetPeriod.Yearly => YearContaining(referenceDate),
            BudgetPeriod.Custom when limit.CustomStartDate is not null && limit.CustomEndDate is not null =>
                new PeriodRange(limit.CustomStartDate.Value, limit.CustomEndDate.Value),
            _ => MonthContaining(referenceDate)
        };

    public async Task<IReadOnlyList<BudgetStatus>> GetStatusesAsync(
        DateOnly referenceDate,
        int? ledgerEntityId = null,
        CancellationToken ct = default)
    {
        var limits = await _db.BudgetLimits
            .Include(b => b.Category)
            .Include(b => b.LedgerEntity)
            .Where(b => b.IsActive)
            .Where(b => ledgerEntityId == null || b.LedgerEntityId == null || b.LedgerEntityId == ledgerEntityId)
            .ToListAsync(ct);

        var results = new List<BudgetStatus>();

        foreach (var limit in limits)
        {
            var period = GetPeriodRange(limit, referenceDate);
            var query = _db.Transactions.AsNoTracking()
                .Where(t => t.CategoryId == limit.CategoryId)
                .Where(t => t.Date >= period.Start && t.Date <= period.End);

            if (limit.LedgerEntityId is not null)
                query = query.Where(t => t.LedgerEntityId == limit.LedgerEntityId);

            var spent = await query.SumAsync(t => (decimal?)Math.Abs(t.Amount), ct) ?? 0m;
            var percent = limit.LimitAmount > 0 ? spent / limit.LimitAmount * 100 : 0;

            results.Add(new BudgetStatus(
                limit,
                spent,
                percent,
                spent > limit.LimitAmount,
                percent >= limit.WarningThresholdPercent && spent <= limit.LimitAmount));
        }

        return results.OrderByDescending(r => r.PercentUsed).ToList();
    }

    private static PeriodRange MonthContaining(DateOnly date) =>
        new(new DateOnly(date.Year, date.Month, 1),
            new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)));

    private static PeriodRange WeekContaining(DateOnly date)
    {
        var day = (int)date.DayOfWeek;
        var start = date.AddDays(-day);
        return new PeriodRange(start, start.AddDays(6));
    }

    private static PeriodRange QuarterContaining(DateOnly date)
    {
        var quarter = (date.Month - 1) / 3;
        var startMonth = quarter * 3 + 1;
        var start = new DateOnly(date.Year, startMonth, 1);
        var endMonth = startMonth + 2;
        return new PeriodRange(start, new DateOnly(date.Year, endMonth, DateTime.DaysInMonth(date.Year, endMonth)));
    }

    private static PeriodRange YearContaining(DateOnly date) =>
        new(new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31));
}

public record MonthlySummary(
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal Net,
    IReadOnlyList<CategorySummary> ByCategory);

public record CategorySummary(string CategoryName, string GroupName, decimal Total, decimal PercentOfIncome, bool IsIncome);

public record YearlySummary(
    int Year,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal Net,
    IReadOnlyList<MonthlyTotals> ByMonth,
    IReadOnlyList<CategorySummary> ByCategory);

public record MonthlyTotals(int Month, string MonthName, decimal Income, decimal Expenses, decimal Net);

public record DayTransactionLine(
    int Id,
    string CategoryName,
    string GroupName,
    decimal Amount,
    string? Notes,
    string? EntityName);

public record DayTransactions(
    DateOnly Date,
    decimal DayIncome,
    decimal DayExpenses,
    IReadOnlyList<DayTransactionLine> Transactions);

public record MonthlyByDayReport(
    int Year,
    int Month,
    IReadOnlyList<DayTransactions> Days,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal Net);

public record SpreadsheetColumn(int CategoryId, string Name, string GroupName, bool IsIncome);

public record SpreadsheetRow(DateOnly Date, IReadOnlyDictionary<int, decimal> AmountByCategory, string? Notes);

public record MonthlySpreadsheetReport(
    int Year,
    int Month,
    IReadOnlyList<SpreadsheetColumn> Columns,
    IReadOnlyList<SpreadsheetRow> Rows,
    decimal TotalIncome,
    IReadOnlyList<decimal> ColumnTotals,
    IReadOnlyList<decimal?> PercentOfIncomeByColumn);

public interface IReportService
{
    Task<MonthlySummary> GetMonthlySummaryAsync(int year, int month, int? ledgerEntityId = null, CancellationToken ct = default);
    Task<YearlySummary> GetYearlySummaryAsync(int year, int? ledgerEntityId = null, CancellationToken ct = default);
    Task<MonthlyByDayReport> GetMonthlyByDayAsync(int year, int month, int? ledgerEntityId = null, CancellationToken ct = default);
    Task<MonthlySpreadsheetReport> GetMonthlySpreadsheetAsync(int year, int month, int? ledgerEntityId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(
        DateOnly? from,
        DateOnly? to,
        int? categoryId,
        int? ledgerEntityId,
        CancellationToken ct = default);
}

public class ReportService : IReportService
{
    private readonly LedgerDbContext _db;

    public ReportService(LedgerDbContext db) => _db = db;

    public async Task<MonthlySummary> GetMonthlySummaryAsync(
        int year,
        int month,
        int? ledgerEntityId = null,
        CancellationToken ct = default)
    {
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var transactions = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Category).ThenInclude(c => c.CategoryGroup)
            .Where(t => t.Date >= start && t.Date <= end)
            .Where(t => ledgerEntityId == null || t.LedgerEntityId == ledgerEntityId)
            .ToListAsync(ct);

        var income = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var expenses = transactions.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount));

        var byCategory = transactions
            .GroupBy(t => new { t.Category.Name, Group = t.Category.CategoryGroup.Name, t.Category.IsIncome })
            .Select(g => new CategorySummary(
                g.Key.Name,
                g.Key.Group,
                g.Sum(t => t.Amount),
                income > 0 ? Math.Abs(g.Sum(t => t.Amount)) / income * 100 : 0,
                g.Key.IsIncome))
            .OrderByDescending(c => Math.Abs(c.Total))
            .ToList();

        return new MonthlySummary(income, expenses, income - expenses, byCategory);
    }

    public async Task<YearlySummary> GetYearlySummaryAsync(
        int year,
        int? ledgerEntityId = null,
        CancellationToken ct = default)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);

        var transactions = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Category).ThenInclude(c => c.CategoryGroup)
            .Where(t => t.Date >= start && t.Date <= end)
            .Where(t => ledgerEntityId == null || t.LedgerEntityId == ledgerEntityId)
            .ToListAsync(ct);

        var income = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var expenses = transactions.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount));

        var byMonth = Enumerable.Range(1, 12).Select(month =>
        {
            var monthTx = transactions.Where(t => t.Date.Month == month).ToList();
            var monthIncome = monthTx.Where(t => t.Amount > 0).Sum(t => t.Amount);
            var monthExpenses = monthTx.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount));
            return new MonthlyTotals(
                month,
                new DateOnly(year, month, 1).ToString("MMM"),
                monthIncome,
                monthExpenses,
                monthIncome - monthExpenses);
        }).ToList();

        var byCategory = transactions
            .GroupBy(t => new { t.Category.Name, Group = t.Category.CategoryGroup.Name, t.Category.IsIncome })
            .Select(g => new CategorySummary(
                g.Key.Name,
                g.Key.Group,
                g.Sum(t => t.Amount),
                income > 0 ? Math.Abs(g.Sum(t => t.Amount)) / income * 100 : 0,
                g.Key.IsIncome))
            .OrderByDescending(c => Math.Abs(c.Total))
            .ToList();

        return new YearlySummary(year, income, expenses, income - expenses, byMonth, byCategory);
    }

    public async Task<MonthlyByDayReport> GetMonthlyByDayAsync(
        int year,
        int month,
        int? ledgerEntityId = null,
        CancellationToken ct = default)
    {
        var transactions = await GetMonthTransactionsAsync(year, month, ledgerEntityId, ct);
        var income = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var expenses = transactions.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount));

        var days = transactions
            .GroupBy(t => t.Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var dayList = g.OrderByDescending(t => t.Id).Select(t => new DayTransactionLine(
                    t.Id,
                    t.Category.Name,
                    t.Category.CategoryGroup.Name,
                    t.Amount,
                    t.Notes,
                    t.LedgerEntity.Name)).ToList();

                return new DayTransactions(
                    g.Key,
                    g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                    g.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount)),
                    dayList);
            })
            .ToList();

        return new MonthlyByDayReport(year, month, days, income, expenses, income - expenses);
    }

    public async Task<MonthlySpreadsheetReport> GetMonthlySpreadsheetAsync(
        int year,
        int month,
        int? ledgerEntityId = null,
        CancellationToken ct = default)
    {
        var transactions = await GetMonthTransactionsAsync(year, month, ledgerEntityId, ct);
        var totalIncome = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var transactionsByDate = transactions.GroupBy(t => t.Date).ToDictionary(g => g.Key, g => g.ToList());

        var columns = transactions
            .GroupBy(t => t.CategoryId)
            .Select(g => g.First().Category)
            .OrderBy(c => c.IsIncome ? 0 : 1)
            .ThenBy(c => c.CategoryGroup.SortOrder)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new SpreadsheetColumn(c.Id, c.Name, c.CategoryGroup.Name, c.IsIncome))
            .ToList();

        var rows = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(day =>
            {
                var date = new DateOnly(year, month, day);
                if (!transactionsByDate.TryGetValue(date, out var dayTransactions))
                {
                    return new SpreadsheetRow(date, new Dictionary<int, decimal>(), null);
                }

                var amountByCategory = dayTransactions
                    .GroupBy(t => t.CategoryId)
                    .ToDictionary(cg => cg.Key, cg => cg.Sum(t => t.Amount));

                var mergedNotes = string.Join(" | ", dayTransactions
                    .OrderBy(t => t.Id)
                    .Select(t => t.Notes?.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n)));

                return new SpreadsheetRow(
                    date,
                    amountByCategory,
                    string.IsNullOrWhiteSpace(mergedNotes) ? null : mergedNotes);
            })
            .ToList();

        var columnTotals = columns
            .Select(col => transactions.Where(t => t.CategoryId == col.CategoryId).Sum(t => t.Amount))
            .ToList();

        var percentOfIncome = columns
            .Zip(columnTotals, (col, total) =>
                col.IsIncome || totalIncome <= 0
                    ? (decimal?)null
                    : Math.Abs(total) / totalIncome * 100)
            .ToList();

        return new MonthlySpreadsheetReport(year, month, columns, rows, totalIncome, columnTotals, percentOfIncome);
    }

    private async Task<List<Transaction>> GetMonthTransactionsAsync(
        int year,
        int month,
        int? ledgerEntityId,
        CancellationToken ct)
    {
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        return await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Category).ThenInclude(c => c.CategoryGroup)
            .Include(t => t.LedgerEntity)
            .Where(t => t.Date >= start && t.Date <= end)
            .Where(t => ledgerEntityId == null || t.LedgerEntityId == ledgerEntityId)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Transaction>> GetTransactionsAsync(
        DateOnly? from,
        DateOnly? to,
        int? categoryId,
        int? ledgerEntityId,
        CancellationToken ct = default)
    {
        var query = _db.Transactions
            .AsNoTracking()
            .Include(t => t.Category).ThenInclude(c => c.CategoryGroup)
            .Include(t => t.LedgerEntity)
            .Include(t => t.Account)
            .AsQueryable();

        if (from is not null) query = query.Where(t => t.Date >= from);
        if (to is not null) query = query.Where(t => t.Date <= to);
        if (categoryId is not null) query = query.Where(t => t.CategoryId == categoryId);
        if (ledgerEntityId is not null) query = query.Where(t => t.LedgerEntityId == ledgerEntityId);

        return await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync(ct);
    }
}
