using Ledger.Core.Entities;
using Ledger.Infrastructure.Services;
using Ledger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ledger.Web.Controllers;

public class ReportsController : Controller
{
    private readonly IReportService _reports;
    private readonly Ledger.Infrastructure.Data.LedgerDbContext _db;

    public ReportsController(IReportService reports, Ledger.Infrastructure.Data.LedgerDbContext db)
    {
        _reports = reports;
        _db = db;
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
}
