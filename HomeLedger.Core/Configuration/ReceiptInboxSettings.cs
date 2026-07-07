namespace HomeLedger.Core.Configuration;

public class ReceiptInboxSettings
{
    public const string SectionName = "ReceiptInbox";

    public const int DefaultMaxFileSizeBytes = 25 * 1024 * 1024;
    public const int DefaultMaxFilesPerUpload = 20;

    public bool Enabled { get; set; }
    public string WatchPath { get; set; } = "./receipts-inbox";
    public int PollIntervalSeconds { get; set; } = 30;
    public int AccountId { get; set; }
    public int LedgerEntityId { get; set; }
    public bool MoveToProcessed { get; set; } = true;
    public string ProcessedFolderName { get; set; } = "processed";
    public int MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
    public int MaxFilesPerUpload { get; set; } = DefaultMaxFilesPerUpload;
}
