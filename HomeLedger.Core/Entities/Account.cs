namespace HomeLedger.Core.Entities;

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public string? AccountNumberLast4 { get; set; }
    public int LedgerEntityId { get; set; }
    public bool IsActive { get; set; } = true;
    public AccountKind Kind { get; set; } = AccountKind.Chequing;
    public int? ImportProfileId { get; set; }

    public LedgerEntity LedgerEntity { get; set; } = null!;
    public ImportProfile? ImportProfile { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
}
