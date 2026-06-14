namespace Ledger.Core.Entities;

public enum ImportBatchStatus
{
    Pending,
    Reviewing,
    Completed,
    Cancelled
}

public enum ImportItemStatus
{
    Pending,
    Accepted,
    Skipped,
    Rejected
}

public class ImportBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public int? AccountId { get; set; }
    public int? LedgerEntityId { get; set; }
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Pending;
    public bool AutoAccept { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public Account? Account { get; set; }
    public LedgerEntity? LedgerEntity { get; set; }
    public ICollection<ImportItem> Items { get; set; } = [];
}

public class ImportItem
{
    public int Id { get; set; }
    public string ImportBatchId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public int? SuggestedCategoryId { get; set; }
    public string? SuggestedNotes { get; set; }
    public ImportItemStatus Status { get; set; } = ImportItemStatus.Pending;
    public int? ResultingTransactionId { get; set; }

    public ImportBatch ImportBatch { get; set; } = null!;
    public Category? SuggestedCategory { get; set; }
}
