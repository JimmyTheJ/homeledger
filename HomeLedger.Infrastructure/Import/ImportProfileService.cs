using HomeLedger.Core.Configuration;
using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Infrastructure.Import;

public interface IImportProfileService
{
    Task<ImportProfile?> GetForAccountAsync(int accountId, int ledgerEntityId, CancellationToken ct = default);
    Task<IReadOnlyList<ImportProfile>> ListAsync(int? ledgerEntityId, CancellationToken ct = default);
    Task<ImportProfile?> GetWithRulesAsync(int profileId, CancellationToken ct = default);
    Task<ImportProfile> CreateAsync(ImportProfile profile, CancellationToken ct = default);
    Task UpdateAsync(ImportProfile profile, CancellationToken ct = default);
    Task<ImportProfile> CreateRbcChequingTemplateAsync(int ledgerEntityId, CancellationToken ct = default);
    Task<ImportSkipRule> AddRuleAsync(ImportSkipRule rule, CancellationToken ct = default);
    Task DeleteRuleAsync(int ruleId, CancellationToken ct = default);
    Task DeactivateAsync(int profileId, CancellationToken ct = default);
}

public class ImportProfileService : IImportProfileService
{
    private readonly HomeLedgerDbContext _db;

    public ImportProfileService(HomeLedgerDbContext db) => _db = db;

    public async Task<ImportProfile?> GetForAccountAsync(int accountId, int ledgerEntityId, CancellationToken ct = default)
    {
        var account = await _db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account?.ImportProfileId is int profileId)
        {
            return await _db.ImportProfiles.AsNoTracking()
                .Include(p => p.Rules.Where(r => r.IsActive))
                .FirstOrDefaultAsync(p => p.Id == profileId && p.IsActive, ct);
        }

        return await _db.ImportProfiles.AsNoTracking()
            .Include(p => p.Rules.Where(r => r.IsActive))
            .Where(p => p.LedgerEntityId == ledgerEntityId && p.IsDefault && p.IsActive)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ImportProfile>> ListAsync(int? ledgerEntityId, CancellationToken ct = default)
    {
        var query = _db.ImportProfiles.AsNoTracking()
            .Include(p => p.Rules)
            .Include(p => p.LedgerEntity)
            .Where(p => p.IsActive);

        if (ledgerEntityId is not null)
            query = query.Where(p => p.LedgerEntityId == ledgerEntityId);

        return await query.OrderBy(p => p.LedgerEntity!.Name).ThenBy(p => p.Name)
            .ToListAsync(ct);
    }

    public Task<ImportProfile?> GetWithRulesAsync(int profileId, CancellationToken ct = default) =>
        _db.ImportProfiles
            .Include(p => p.Rules.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
            .Include(p => p.LedgerEntity)
            .FirstOrDefaultAsync(p => p.Id == profileId, ct);

    public async Task<ImportProfile> CreateAsync(ImportProfile profile, CancellationToken ct = default)
    {
        if (profile.IsDefault)
            await ClearDefaultAsync(profile.LedgerEntityId, excludeProfileId: null, ct);

        _db.ImportProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task UpdateAsync(ImportProfile profile, CancellationToken ct = default)
    {
        if (profile.IsDefault)
            await ClearDefaultAsync(profile.LedgerEntityId, profile.Id, ct);

        _db.ImportProfiles.Update(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ImportProfile> CreateRbcChequingTemplateAsync(int ledgerEntityId, CancellationToken ct = default)
    {
        var profile = new ImportProfile
        {
            Name = "RBC chequing (skip transfers & card payments)",
            LedgerEntityId = ledgerEntityId,
            UseLlmForUnmatched = true
        };

        var rules = new[]
        {
            ("Credit card payment", @"(?i)www payment.*mastercard", ImportSkipRuleMatchType.Regex, ImportSkipReasons.CreditCardPayment, 10),
            ("Internal WWW transfer", "WWW TRANSFER", ImportSkipRuleMatchType.Contains, ImportSkipReasons.InternalTransfer, 20),
            ("Internal DDA transfer", "WWW TRF DDA", ImportSkipRuleMatchType.Contains, ImportSkipReasons.InternalTransfer, 30),
            ("Investment transfer", "WS INVESTMENTS", ImportSkipRuleMatchType.Contains, ImportSkipReasons.InvestmentTransfer, 40),
            ("Investment (type)", "INVESTMENT", ImportSkipRuleMatchType.Contains, ImportSkipReasons.InvestmentTransfer, 50),
            ("Health reimbursement", "HEALTH/DENTAL CLAIM", ImportSkipRuleMatchType.Contains, ImportSkipReasons.Reimbursement, 60),
            ("Manulife reimbursement", "MANULIFE", ImportSkipRuleMatchType.Contains, ImportSkipReasons.Reimbursement, 70),
        };

        var sort = 0;
        foreach (var (name, pattern, matchType, skipKind, order) in rules)
        {
            profile.Rules.Add(new ImportSkipRule
            {
                Name = name,
                Pattern = pattern,
                MatchType = matchType,
                SkipKind = skipKind,
                SortOrder = order == 0 ? sort++ : order
            });
        }

        _db.ImportProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<ImportSkipRule> AddRuleAsync(ImportSkipRule rule, CancellationToken ct = default)
    {
        _db.ImportSkipRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task DeleteRuleAsync(int ruleId, CancellationToken ct = default)
    {
        var rule = await _db.ImportSkipRules.FindAsync([ruleId], ct);
        if (rule is null)
            return;

        _db.ImportSkipRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(int profileId, CancellationToken ct = default)
    {
        var profile = await _db.ImportProfiles.FindAsync([profileId], ct);
        if (profile is null)
            return;

        profile.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    private async Task ClearDefaultAsync(int ledgerEntityId, int? excludeProfileId, CancellationToken ct)
    {
        var others = await _db.ImportProfiles
            .Where(p => p.LedgerEntityId == ledgerEntityId && p.IsDefault && p.Id != excludeProfileId)
            .ToListAsync(ct);

        foreach (var other in others)
            other.IsDefault = false;
    }
}
