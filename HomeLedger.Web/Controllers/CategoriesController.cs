using HomeLedger.Core.Entities;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Services;
using HomeLedger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Web.Controllers;

public class CategoriesController : Controller
{
    private readonly HomeLedgerDbContext _db;
    private readonly ICategoryService _categories;

    public CategoriesController(HomeLedgerDbContext db, ICategoryService categories)
    {
        _db = db;
        _categories = categories;
    }

    public async Task<IActionResult> Index(int? entityId, CancellationToken ct)
    {
        await PopulateScopeLookupsAsync(ct);
        var groups = await _categories.GetGroupsAsync(entityId, includeInactive: true, ct);
        ViewBag.ScopeEntityId = entityId;
        ViewBag.ScopeLabel = entityId is null ? "Global baseline" : await _db.Entities.Where(e => e.Id == entityId).Select(e => e.Name).FirstOrDefaultAsync(ct);
        return View(groups);
    }

    public async Task<IActionResult> CreateGroup(int? entityId, CancellationToken ct)
    {
        await PopulateScopeLookupsAsync(ct);
        return View(new CategoryGroupFormModel { LedgerEntityId = entityId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGroup(CategoryGroupFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateScopeLookupsAsync(ct);
            return View(model);
        }

        var sortOrder = await _db.CategoryGroups
            .Where(g => g.LedgerEntityId == model.LedgerEntityId)
            .Select(g => (int?)g.SortOrder)
            .MaxAsync(ct) ?? -1;

        _db.CategoryGroups.Add(new CategoryGroup
        {
            Name = model.Name.Trim(),
            IsIncome = model.IsIncome,
            LedgerEntityId = model.LedgerEntityId,
            SortOrder = sortOrder + 1
        });
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index), new { entityId = model.LedgerEntityId });
    }

    public async Task<IActionResult> EditGroup(int id, CancellationToken ct)
    {
        var group = await _db.CategoryGroups.FindAsync([id], ct);
        if (group is null) return NotFound();

        await PopulateScopeLookupsAsync(ct);
        return View(new CategoryGroupFormModel
        {
            Id = group.Id,
            Name = group.Name,
            IsIncome = group.IsIncome,
            LedgerEntityId = group.LedgerEntityId,
            SortOrder = group.SortOrder
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditGroup(int id, CategoryGroupFormModel model, CancellationToken ct)
    {
        var group = await _db.CategoryGroups.FindAsync([id], ct);
        if (group is null) return NotFound();

        if (!ModelState.IsValid)
        {
            await PopulateScopeLookupsAsync(ct);
            return View(model);
        }

        group.Name = model.Name.Trim();
        group.IsIncome = model.IsIncome;
        group.SortOrder = model.SortOrder;
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index), new { entityId = group.LedgerEntityId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGroup(int id, CancellationToken ct)
    {
        var group = await _db.CategoryGroups
            .Include(g => g.Categories)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();

        var categoryIds = group.Categories.Select(c => c.Id).ToList();
        var hasTransactions = await _db.Transactions.AnyAsync(
            t => t.CategoryId != null && categoryIds.Contains(t.CategoryId.Value), ct);
        if (hasTransactions)
        {
            group.IsActive = false;
            foreach (var category in group.Categories)
                category.IsActive = false;
        }
        else
        {
            _db.Categories.RemoveRange(group.Categories);
            _db.CategoryGroups.Remove(group);
        }

        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index), new { entityId = group.LedgerEntityId });
    }

    public async Task<IActionResult> CreateCategory(int? entityId, int? groupId, CancellationToken ct)
    {
        await PopulateCategoryLookupsAsync(entityId, ct);
        return View(new CategoryFormModel { LedgerEntityId = entityId, CategoryGroupId = groupId ?? 0 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(CategoryFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCategoryLookupsAsync(model.LedgerEntityId, ct);
            return View(model);
        }

        var group = await _db.CategoryGroups.FindAsync([model.CategoryGroupId], ct);
        if (group is null) return NotFound();

        var sortOrder = await _db.Categories
            .Where(c => c.CategoryGroupId == model.CategoryGroupId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync(ct) ?? -1;

        _db.Categories.Add(new Category
        {
            Name = model.Name.Trim(),
            CategoryGroupId = model.CategoryGroupId,
            LedgerEntityId = model.LedgerEntityId,
            IsIncome = group.IsIncome,
            SortOrder = sortOrder + 1
        });
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index), new { entityId = model.LedgerEntityId });
    }

    public async Task<IActionResult> EditCategory(int id, CancellationToken ct)
    {
        var category = await _db.Categories.FindAsync([id], ct);
        if (category is null) return NotFound();

        await PopulateCategoryLookupsAsync(category.LedgerEntityId, ct);
        return View(new CategoryFormModel
        {
            Id = category.Id,
            Name = category.Name,
            CategoryGroupId = category.CategoryGroupId,
            LedgerEntityId = category.LedgerEntityId,
            SortOrder = category.SortOrder
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(int id, CategoryFormModel model, CancellationToken ct)
    {
        var category = await _db.Categories.FindAsync([id], ct);
        if (category is null) return NotFound();

        if (!ModelState.IsValid)
        {
            await PopulateCategoryLookupsAsync(model.LedgerEntityId, ct);
            return View(model);
        }

        category.Name = model.Name.Trim();
        category.CategoryGroupId = model.CategoryGroupId;
        category.SortOrder = model.SortOrder;
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index), new { entityId = category.LedgerEntityId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        var category = await _db.Categories.FindAsync([id], ct);
        if (category is null) return NotFound();

        var hasTransactions = await _db.Transactions.AnyAsync(t => t.CategoryId == id, ct);
        if (hasTransactions)
            category.IsActive = false;
        else
            _db.Categories.Remove(category);

        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index), new { entityId = category.LedgerEntityId });
    }

    private async Task PopulateScopeLookupsAsync(CancellationToken ct)
    {
        ViewBag.Entities = await _db.Entities.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync(ct);
    }

    private async Task PopulateCategoryLookupsAsync(int? entityId, CancellationToken ct)
    {
        await PopulateScopeLookupsAsync(ct);
        var groups = await _categories.GetGroupsAsync(entityId, includeInactive: false, ct);
        ViewBag.Groups = new SelectList(groups, "Id", "Name");
    }
}
