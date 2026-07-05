using HomeLedger.Core.Import;

namespace HomeLedger.Core.Entities;

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
    public long? FileSizeBytes { get; set; }
    public string? FileSha256 { get; set; }
    public int? AccountId { get; set; }
    public int? LedgerEntityId { get; set; }
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Pending;
    public bool AutoAccept { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool LlmConfiguredAtImport { get; set; }
    public bool LlmCategorizationAvailable { get; set; }
    public bool LlmClassificationAvailable { get; set; }
    public int LlmCategorizedCount { get; set; }
    public int LlmClassifiedCount { get; set; }
    public bool PdfExtractedWithLlm { get; set; }
    public string? LlmAvailabilityNotes { get; set; }
    public ImportKind ImportKind { get; set; } = ImportKind.Csv;
    public string? Merchant { get; set; }
    public string? SourcePath { get; set; }

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
    public string? SuggestionSource { get; set; }
    public ImportItemStatus Status { get; set; } = ImportItemStatus.Pending;
    public string? SkipReason { get; set; }
    public string? SuggestedSkipReason { get; set; }
    public int? ResultingTransactionId { get; set; }
    public string? Merchant { get; set; }
    public string? SourceFileName { get; set; }

    public ImportBatch ImportBatch { get; set; } = null!;
    public Category? SuggestedCategory { get; set; }
}
