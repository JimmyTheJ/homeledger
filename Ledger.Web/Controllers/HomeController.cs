using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Services;
using Ledger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Web.Controllers;

public class HomeController : Controller
{
    private readonly IReportService _reports;
    private readonly IBudgetService _budgets;
    private readonly LedgerDbContext _db;

    public HomeController(IReportService reports, IBudgetService budgets, LedgerDbContext db)
    {
        _reports = reports;
        _budgets = budgets;
        _db = db;
    }

    public async Task<IActionResult> Index(int? year, int? month, int? entityId, CancellationToken ct)
    {
        var now = DateOnly.FromDateTime(DateTime.Today);
        var y = year ?? now.Year;
        var m = month ?? now.Month;

        var summary = await _reports.GetMonthlySummaryAsync(y, m, entityId, ct);
        var reference = new DateOnly(y, m, 1);
        var budgetStatuses = await _budgets.GetStatusesAsync(reference, entityId, ct);
        var entities = await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct);

        var vm = new DashboardViewModel
        {
            Year = y,
            Month = m,
            LedgerEntityId = entityId,
            Summary = summary,
            BudgetStatuses = budgetStatuses,
            Entities = entities
        };

        return View(vm);
    }
}
