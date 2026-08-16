using HomeLedger.Infrastructure.Import;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class ReceiptImportJobQueueTests
{
    [Fact]
    public void WithoutSavedReceipts_keeps_active_and_failed_jobs()
    {
        var jobs = new[]
        {
            Job("queued", ReceiptImportJobStatus.Queued),
            Job("processing", ReceiptImportJobStatus.Processing),
            Job("failed", ReceiptImportJobStatus.Failed, "batch-failed")
        };

        var visible = ReceiptImportJobQueue.WithoutSavedReceipts(jobs, new HashSet<string>());

        Assert.Equal(["queued", "processing", "failed"], visible.Select(j => j.Id).ToArray());
    }

    [Fact]
    public void WithoutSavedReceipts_keeps_completed_jobs_still_awaiting_review()
    {
        var jobs = new[]
        {
            Job("ready", ReceiptImportJobStatus.Completed, "batch-pending")
        };

        var visible = ReceiptImportJobQueue.WithoutSavedReceipts(jobs, new HashSet<string> { "batch-pending" });

        Assert.Single(visible);
        Assert.Equal("ready", visible[0].Id);
    }

    [Fact]
    public void WithoutSavedReceipts_hides_completed_jobs_after_the_receipt_is_saved()
    {
        var jobs = new[]
        {
            Job("saved", ReceiptImportJobStatus.Completed, "batch-saved"),
            Job("ready", ReceiptImportJobStatus.Completed, "batch-pending"),
            Job("extracting", ReceiptImportJobStatus.Processing)
        };

        var visible = ReceiptImportJobQueue.WithoutSavedReceipts(
            jobs,
            new HashSet<string> { "batch-pending" });

        Assert.Equal(["ready", "extracting"], visible.Select(j => j.Id).ToArray());
    }

    [Fact]
    public void WithoutSavedReceipts_hides_completed_jobs_with_no_batch()
    {
        var jobs = new[]
        {
            Job("orphaned", ReceiptImportJobStatus.Completed)
        };

        var visible = ReceiptImportJobQueue.WithoutSavedReceipts(jobs, new HashSet<string>());

        Assert.Empty(visible);
    }

    private static ReceiptImportJobSnapshot Job(
        string id,
        ReceiptImportJobStatus status,
        string? resultBatchId = null) =>
        new(
            id,
            "receipt.jpg",
            status,
            Error: null,
            Warning: null,
            resultBatchId,
            LineItemCount: 2,
            CreatedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow);
}
