namespace HomeLedger.Core.Entities;

public class ImportProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int LedgerEntityId { get; set; }
    public bool IsDefault { get; set; }
    public bool UseLlmForUnmatched { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public LedgerEntity LedgerEntity { get; set; } = null!;
    public ICollection<ImportSkipRule> Rules { get; set; } = [];
    public ICollection<Account> Accounts { get; set; } = [];
}

public enum ImportSkipRuleMatchType
{
    Contains,
    Regex
}

public class ImportSkipRule
{
    public int Id { get; set; }
    public int ImportProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public ImportSkipRuleMatchType MatchType { get; set; } = ImportSkipRuleMatchType.Contains;
    public string SkipKind { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ImportProfile ImportProfile { get; set; } = null!;
}
