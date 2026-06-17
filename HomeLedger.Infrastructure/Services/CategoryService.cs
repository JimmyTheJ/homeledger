using HomeLedger.Core.Entities;
using HomeLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Infrastructure.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryGroup>> GetGroupsAsync(int? ledgerEntityId, bool includeInactive = false, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(int? ledgerEntityId, bool includeInactive = false, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetSelectableCategoriesAsync(int? ledgerEntityId, CancellationToken ct = default);
}

public class CategoryService : ICategoryService
{
    private readonly HomeLedgerDbContext _db;

    public CategoryService(HomeLedgerDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategoryGroup>> GetGroupsAsync(
        int? ledgerEntityId,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _db.CategoryGroups
            .AsNoTracking()
            .Include(g => g.Categories.Where(c => includeInactive || c.IsActive))
            .Where(g => g.LedgerEntityId == ledgerEntityId);

        if (!includeInactive)
            query = query.Where(g => g.IsActive);

        return await query
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name)
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(
        int? ledgerEntityId,
        bool includeInactive = false,
        CancellationToken ct = default) =>
        QueryCategoriesAsync(ledgerEntityId, includeInactive, globalOnly: ledgerEntityId is null, ct);

    public Task<IReadOnlyList<Category>> GetSelectableCategoriesAsync(
        int? ledgerEntityId,
        CancellationToken ct = default) =>
        QueryCategoriesAsync(ledgerEntityId, includeInactive: false, globalOnly: false, ct);

    private async Task<IReadOnlyList<Category>> QueryCategoriesAsync(
        int? ledgerEntityId,
        bool includeInactive,
        bool globalOnly,
        CancellationToken ct)
    {
        var query = _db.Categories
            .AsNoTracking()
            .Include(c => c.CategoryGroup)
            .AsQueryable();

        if (globalOnly)
            query = query.Where(c => c.LedgerEntityId == null);
        else if (ledgerEntityId is null)
            query = query.Where(c => c.LedgerEntityId == null);
        else
            query = query.Where(c => c.LedgerEntityId == null || c.LedgerEntityId == ledgerEntityId);

        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        return await query
            .OrderBy(c => c.CategoryGroup.SortOrder)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);
    }
}
