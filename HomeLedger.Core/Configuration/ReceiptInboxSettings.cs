namespace HomeLedger.Core.Configuration;

public class ReceiptInboxSettings
{
    public const string SectionName = "ReceiptInbox";

    public bool Enabled { get; set; }
    public string WatchPath { get; set; } = "./receipts-inbox";
    public int PollIntervalSeconds { get; set; } = 30;
    public int AccountId { get; set; }
    public int LedgerEntityId { get; set; }
    public bool MoveToProcessed { get; set; } = true;
    public string ProcessedFolderName { get; set; } = "processed";
}
