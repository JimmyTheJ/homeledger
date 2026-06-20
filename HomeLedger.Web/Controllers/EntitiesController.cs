using HomeLedger.Core.Entities;
using HomeLedger.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Web.Controllers;

public class EntitiesController : Controller
{
    private readonly HomeLedgerDbContext _db;

    public EntitiesController(HomeLedgerDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var entities = await _db.Entities.AsNoTracking()
            .Include(e => e.Accounts.Where(a => a.IsActive))
                .ThenInclude(a => a.ImportProfile)
            .Where(e => e.IsActive)
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        return View(entities);
    }

    public IActionResult Create() => View(new LedgerEntity());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LedgerEntity model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), "Name is required.");

        if (!ModelState.IsValid)
            return View(model);

        _db.Entities.Add(new LedgerEntity
        {
            Name = model.Name.Trim(),
            Color = model.Color
        });
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> CreateAccount(int entityId, CancellationToken ct)
    {
        var entity = await _db.Entities.FindAsync([entityId], ct);
        if (entity is null) return NotFound();

        await PopulateProfileLookupsAsync(entityId, ct);
        PopulateAccountKindLookups();
        return View(new Account { LedgerEntityId = entityId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(Account model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), "Name is required.");

        if (!ModelState.IsValid)
        {
            await PopulateProfileLookupsAsync(model.LedgerEntityId, ct);
            PopulateAccountKindLookups();
            return View(model);
        }

        _db.Accounts.Add(new Account
        {
            Name = model.Name.Trim(),
            Institution = string.IsNullOrWhiteSpace(model.Institution) ? null : model.Institution.Trim(),
            AccountNumberLast4 = string.IsNullOrWhiteSpace(model.AccountNumberLast4) ? null : model.AccountNumberLast4.Trim(),
            LedgerEntityId = model.LedgerEntityId,
            ImportProfileId = model.ImportProfileId,
            Kind = model.Kind
        });
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> EditAccount(int id, CancellationToken ct)
    {
        var account = await _db.Accounts.FindAsync([id], ct);
        if (account is null) return NotFound();

        await PopulateProfileLookupsAsync(account.LedgerEntityId, ct);
        PopulateAccountKindLookups();
        return View(account);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAccount(int id, Account model, CancellationToken ct)
    {
        var account = await _db.Accounts.FindAsync([id], ct);
        if (account is null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), "Name is required.");

        if (!ModelState.IsValid)
        {
            await PopulateProfileLookupsAsync(account.LedgerEntityId, ct);
            PopulateAccountKindLookups();
            return View(model);
        }

        account.Name = model.Name.Trim();
        account.Institution = string.IsNullOrWhiteSpace(model.Institution) ? null : model.Institution.Trim();
        account.AccountNumberLast4 = string.IsNullOrWhiteSpace(model.AccountNumberLast4) ? null : model.AccountNumberLast4.Trim();
        account.ImportProfileId = model.ImportProfileId;
        account.Kind = model.Kind;
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateProfileLookupsAsync(int entityId, CancellationToken ct)
    {
        var profiles = await _db.ImportProfiles.AsNoTracking()
            .Where(p => p.IsActive && p.LedgerEntityId == entityId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        ViewBag.ImportProfiles = new SelectList(profiles, "Id", "Name");
    }

    private void PopulateAccountKindLookups()
    {
        ViewBag.AccountKinds = new SelectList(
            Enum.GetValues<AccountKind>()
                .Select(k => new { Id = (int)k, Name = AccountKinds.Label(k) }),
            "Id", "Name");
    }
}
