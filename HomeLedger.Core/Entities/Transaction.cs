namespace HomeLedger.Core.Entities;

public class Transaction
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public int CategoryId { get; set; }
    public int LedgerEntityId { get; set; }
    public int? AccountId { get; set; }
    public string? Notes { get; set; }
    public string? ExternalId { get; set; }
    public string? ImportBatchId { get; set; }
    public int? LinkedTransactionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public LedgerEntity LedgerEntity { get; set; } = null!;
    public Account? Account { get; set; }
}
