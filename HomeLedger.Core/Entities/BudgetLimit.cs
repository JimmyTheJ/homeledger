namespace HomeLedger.Core.Entities;

public enum BudgetPeriod
{
    Weekly,
    Monthly,
    Quarterly,
    Yearly,
    Custom
}

public class BudgetLimit
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int? LedgerEntityId { get; set; }
    public decimal LimitAmount { get; set; }
    public decimal WarningThresholdPercent { get; set; } = 80;
    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;
    public DateOnly? CustomStartDate { get; set; }
    public DateOnly? CustomEndDate { get; set; }
    public bool IsActive { get; set; } = true;

    public Category Category { get; set; } = null!;
    public LedgerEntity? LedgerEntity { get; set; }
}
