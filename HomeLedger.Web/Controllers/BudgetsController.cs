using HomeLedger.Core.Entities;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Services;
using HomeLedger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Web.Controllers;

public class BudgetsController : Controller
{
    private readonly HomeLedgerDbContext _db;
    private readonly IBudgetService _budgets;
    private readonly ICategoryService _categories;

    public BudgetsController(HomeLedgerDbContext db, IBudgetService budgets, ICategoryService categories)
    {
        _db = db;
        _budgets = budgets;
        _categories = categories;
    }

    public async Task<IActionResult> Index(int? entityId, CancellationToken ct)
    {
        var reference = DateOnly.FromDateTime(DateTime.Today);
        var statuses = await _budgets.GetStatusesAsync(reference, entityId, ct);
        var limits = await _db.BudgetLimits
            .Include(b => b.Category)
            .Include(b => b.LedgerEntity)
            .Where(b => b.IsActive)
            .OrderBy(b => b.Category.Name)
            .ToListAsync(ct);

        ViewBag.Statuses = statuses;
        ViewBag.EntityId = entityId;
        ViewBag.Entities = await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct);
        return View(limits);
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateLookupsAsync(null, ct);
        return View(new BudgetLimitFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BudgetLimitFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(model.LedgerEntityId, ct);
            return View(model);
        }

        _db.BudgetLimits.Add(new BudgetLimit
        {
            CategoryId = model.CategoryId,
            LedgerEntityId = model.LedgerEntityId,
            LimitAmount = model.LimitAmount,
            WarningThresholdPercent = model.WarningThresholdPercent,
            Period = model.Period,
            CustomStartDate = model.CustomStartDate,
            CustomEndDate = model.CustomEndDate
        });
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var limit = await _db.BudgetLimits.FindAsync([id], ct);
        if (limit is not null)
        {
            limit.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateLookupsAsync(int? entityId, CancellationToken ct)
    {
        var categoryList = (await _categories.GetSelectableCategoriesAsync(entityId, ct))
            .Where(c => !c.IsIncome)
            .ToList();
        ViewBag.Categories = new SelectList(categoryList, "Id", "Name");
        ViewBag.Entities = new SelectList(
            await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct),
            "Id", "Name");
    }
}

