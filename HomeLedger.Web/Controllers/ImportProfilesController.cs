using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Import;
using HomeLedger.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Web.Controllers;

public class ImportProfilesController : Controller
{
    private readonly HomeLedgerDbContext _db;
    private readonly IImportProfileService _profiles;

    public ImportProfilesController(HomeLedgerDbContext db, IImportProfileService profiles)
    {
        _db = db;
        _profiles = profiles;
    }

    public async Task<IActionResult> Index(int? entityId, CancellationToken ct)
    {
        await PopulateEntityLookupsAsync(ct);
        var profiles = await _profiles.ListAsync(entityId, ct);
        ViewBag.EntityId = entityId;
        return View(profiles);
    }

    public async Task<IActionResult> Create(int? entityId, CancellationToken ct)
    {
        await PopulateEntityLookupsAsync(ct);
        return View(new ImportProfile { LedgerEntityId = entityId ?? 0, UseLlmForUnmatched = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImportProfile model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), "Name is required.");
        if (model.LedgerEntityId <= 0)
            ModelState.AddModelError(nameof(model.LedgerEntityId), "Entity is required.");

        if (!ModelState.IsValid)
        {
            await PopulateEntityLookupsAsync(ct);
            return View(model);
        }

        await _profiles.CreateAsync(new ImportProfile
        {
            Name = model.Name.Trim(),
            LedgerEntityId = model.LedgerEntityId,
            IsDefault = model.IsDefault,
            UseLlmForUnmatched = model.UseLlmForUnmatched
        }, ct);

        TempData[FlashMessage.SuccessKey] = "Import profile created.";
        return RedirectToAction(nameof(Index), new { entityId = model.LedgerEntityId });
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var profile = await _profiles.GetWithRulesAsync(id, ct);
        if (profile is null) return NotFound();

        await PopulateEntityLookupsAsync(ct);
        ViewBag.SkipKinds = BuildSkipKindSelectList();
        return View(profile);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ImportProfile model, CancellationToken ct)
    {
        var profile = await _profiles.GetWithRulesAsync(id, ct);
        if (profile is null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), "Name is required.");

        if (!ModelState.IsValid)
        {
            await PopulateEntityLookupsAsync(ct);
            ViewBag.SkipKinds = BuildSkipKindSelectList();
            return View(profile);
        }

        profile.Name = model.Name.Trim();
        profile.IsDefault = model.IsDefault;
        profile.UseLlmForUnmatched = model.UseLlmForUnmatched;
        await _profiles.UpdateAsync(profile, ct);

        TempData[FlashMessage.SuccessKey] = "Import profile updated.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRule(int profileId, string name, string pattern, ImportSkipRuleMatchType matchType, string skipKind, CancellationToken ct)
    {
        var profile = await _profiles.GetWithRulesAsync(profileId, ct);
        if (profile is null) return NotFound();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(skipKind))
        {
            TempData[FlashMessage.ErrorKey] = "Rule name, pattern, and skip type are required.";
            return RedirectToAction(nameof(Edit), new { id = profileId });
        }

        var maxOrder = profile.Rules.Select(r => r.SortOrder).DefaultIfEmpty(0).Max();
        await _profiles.AddRuleAsync(new ImportSkipRule
        {
            ImportProfileId = profileId,
            Name = name.Trim(),
            Pattern = pattern.Trim(),
            MatchType = matchType,
            SkipKind = skipKind,
            SortOrder = maxOrder + 10
        }, ct);

        TempData[FlashMessage.SuccessKey] = "Skip rule added.";
        return RedirectToAction(nameof(Edit), new { id = profileId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRule(int profileId, int ruleId, CancellationToken ct)
    {
        await _profiles.DeleteRuleAsync(ruleId, ct);
        TempData[FlashMessage.SuccessKey] = "Skip rule removed.";
        return RedirectToAction(nameof(Edit), new { id = profileId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRbcTemplate(int entityId, CancellationToken ct)
    {
        if (entityId <= 0)
        {
            TempData[FlashMessage.ErrorKey] = "Select an entity first.";
            return RedirectToAction(nameof(Index));
        }

        var profile = await _profiles.CreateRbcChequingTemplateAsync(entityId, ct);
        TempData[FlashMessage.SuccessKey] = "RBC chequing template profile created.";
        return RedirectToAction(nameof(Edit), new { id = profile.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var profile = await _profiles.GetWithRulesAsync(id, ct);
        if (profile is null) return NotFound();

        await _profiles.DeactivateAsync(id, ct);
        TempData[FlashMessage.SuccessKey] = "Import profile deactivated.";
        return RedirectToAction(nameof(Index), new { entityId = profile.LedgerEntityId });
    }

    private async Task PopulateEntityLookupsAsync(CancellationToken ct)
    {
        ViewBag.Entities = new SelectList(
            await _db.Entities.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync(ct),
            "Id", "Name");
    }

    private static SelectList BuildSkipKindSelectList() => new(
        new[]
        {
            ImportSkipReasons.CreditCardPayment,
            ImportSkipReasons.InternalTransfer,
            ImportSkipReasons.InvestmentTransfer,
            ImportSkipReasons.Reimbursement
        }.Select(k => new { Id = k, Name = ImportSkipReasons.Describe(k) }),
        "Id", "Name");
}
