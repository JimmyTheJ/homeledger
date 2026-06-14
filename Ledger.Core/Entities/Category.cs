namespace Ledger.Core.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryGroupId { get; set; }
    public int SortOrder { get; set; }
    public bool IsIncome { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Null = global baseline; set = entity-specific extension.</summary>
    public int? LedgerEntityId { get; set; }

    public CategoryGroup CategoryGroup { get; set; } = null!;
    public LedgerEntity? LedgerEntity { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
    public ICollection<BudgetLimit> BudgetLimits { get; set; } = [];
}
