namespace HomeLedger.Core.Entities;

/// <summary>
/// A person or financial entity whose transactions are tracked separately
/// (e.g. household members with split finances).
/// </summary>
public class LedgerEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Account> Accounts { get; set; } = [];
    public ICollection<Transaction> Transactions { get; set; } = [];
}
