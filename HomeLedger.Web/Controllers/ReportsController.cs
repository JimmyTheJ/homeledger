using HomeLedger.Infrastructure.Services;
using HomeLedger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Web.Controllers;

public class ReportsController : Controller
{
    private readonly IReportService _reports;
    private readonly HomeLedger.Infrastructure.Data.HomeLedgerDbContext _db;

    public ReportsController(IReportService reports, HomeLedger.Infrastructure.Data.HomeLedgerDbContext db)
    {
        _reports = reports;
        _db = db;
    }

    public async Task<IActionResult> Month(int? year, int? month, int? entityId, CancellationToken ct)
    {
        var (y, m, vm) = await BuildMonthViewModelAsync(year, month, entityId, ct);
        var report = await _reports.GetMonthlyByDayAsync(y, m, entityId, ct);
        ViewBag.Report = report;
        return View(vm);
    }

    public async Task<IActionResult> Spreadsheet(int? year, int? month, int? entityId, bool showAllCategories = false, CancellationToken ct = default)
    {
        var (y, m, vm) = await BuildMonthViewModelAsync(year, month, entityId, ct);
        var report = await _reports.GetMonthlySpreadsheetAsync(y, m, entityId, showAllCategories, ct);
        ViewBag.Report = report;
        ViewBag.ShowAllCategories = showAllCategories;
        return View(vm);
    }

    public async Task<IActionResult> Year(int? year, int? entityId, CancellationToken ct)
    {
        var y = year ?? DateOnly.FromDateTime(DateTime.Today).Year;
        var summary = await _reports.GetYearlySummaryAsync(y, entityId, ct);
        var entities = await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct);

        return View(new YearlyReportViewModel
        {
            Year = y,
            LedgerEntityId = entityId,
            Summary = summary,
            Entities = entities
        });
    }

    private async Task<(int Year, int Month, MonthReportViewModel ViewModel)> BuildMonthViewModelAsync(
        int? year,
        int? month,
        int? entityId,
        CancellationToken ct)
    {
        var now = DateOnly.FromDateTime(DateTime.Today);
        var y = year ?? now.Year;
        var m = month ?? now.Month;
        var entities = await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct);

        return (y, m, new MonthReportViewModel
        {
            Year = y,
            Month = m,
            LedgerEntityId = entityId,
            Entities = entities
        });
    }
}
