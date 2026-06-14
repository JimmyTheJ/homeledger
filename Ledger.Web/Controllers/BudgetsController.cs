using Ledger.Core.Entities;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Services;
using Ledger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Web.Controllers;

public class BudgetsController : Controller
{
    private readonly LedgerDbContext _db;
    private readonly IBudgetService _budgets;
    private readonly ICategoryService _categories;

    public BudgetsController(LedgerDbContext db, IBudgetService budgets, ICategoryService categories)
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

public class EntitiesController : Controller
{
    private readonly LedgerDbContext _db;

    public EntitiesController(LedgerDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await _db.Entities.Include(e => e.Accounts).OrderBy(e => e.Name).ToListAsync(ct));

    public IActionResult Create() => View(new LedgerEntity());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LedgerEntity model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.Entities.Add(model);
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> CreateAccount(int entityId, CancellationToken ct)
    {
        ViewBag.EntityId = entityId;
        return View(new Account { LedgerEntityId = entityId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(Account model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.Accounts.Add(model);
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }
}

public class SettingsController : Controller
{
  private readonly IConfiguration _configuration;

  public SettingsController(IConfiguration configuration) => _configuration = configuration;

  public IActionResult Index()
  {
    var llm = _configuration.GetSection(Ledger.Core.Configuration.LlmSettings.SectionName)
      .Get<Ledger.Core.Configuration.LlmSettings>() ?? new();
    return View(llm);
  }
}
