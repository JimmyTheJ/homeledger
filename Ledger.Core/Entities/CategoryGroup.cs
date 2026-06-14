namespace Ledger.Core.Entities;

public class CategoryGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsIncome { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Null = global baseline; set = entity-specific extension.</summary>
    public int? LedgerEntityId { get; set; }

    public LedgerEntity? LedgerEntity { get; set; }
    public ICollection<Category> Categories { get; set; } = [];
}
