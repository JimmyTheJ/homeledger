using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace HomeLedger.Infrastructure.Import;

public enum ReceiptImportJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public sealed record ReceiptImportEnqueueRequest(
    string FileName,
    byte[] Content,
    string? ContentType,
    int AccountId,
    int LedgerEntityId,
    bool AutoAccept);

public sealed record ReceiptImportJobSnapshot(
    string Id,
    string FileName,
    ReceiptImportJobStatus Status,
    string? Error,
    string? Warning,
    string? ResultBatchId,
    int? LineItemCount,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc)
{
    public bool IsActive => Status is ReceiptImportJobStatus.Queued or ReceiptImportJobStatus.Processing;
}

public interface IReceiptImportJobQueue
{
    IReadOnlyList<ReceiptImportJobSnapshot> Enqueue(IEnumerable<ReceiptImportEnqueueRequest> requests);
    IAsyncEnumerable<string> ReadJobIdsAsync(CancellationToken ct);
    bool TryGetJob(string id, out ReceiptImportJobWorkItem? job);
    void MarkProcessing(string id);
    void MarkCompleted(string id, string batchId, int lineItemCount, string? warning = null);
    void MarkFailed(string id, string error);
    IReadOnlyList<ReceiptImportJobSnapshot> GetVisibleJobs();
    bool HasActiveJobs { get; }
}

public sealed class ReceiptImportJobWorkItem
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string TempFilePath { get; init; }
    public string? ContentType { get; init; }
    public required int AccountId { get; init; }
    public required int LedgerEntityId { get; init; }
    public required bool AutoAccept { get; init; }
    public ReceiptImportJobStatus Status { get; set; } = ReceiptImportJobStatus.Queued;
    public string? Error { get; set; }
    public string? Warning { get; set; }
    public string? ResultBatchId { get; set; }
    public int? LineItemCount { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class ReceiptImportJobQueue : IReceiptImportJobQueue
{
    private static readonly TimeSpan VisibleCompletedFor = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, ReceiptImportJobWorkItem> _jobs = new();
    private readonly Channel<string> _ids = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly string _tempRoot;

    public ReceiptImportJobQueue(IHostEnvironment environment)
    {
        _tempRoot = Path.Combine(environment.ContentRootPath, "data", "receipt-import-tmp");
        Directory.CreateDirectory(_tempRoot);
        ClearOrphanedTempFiles();
    }

    public IReadOnlyList<ReceiptImportJobSnapshot> Enqueue(IEnumerable<ReceiptImportEnqueueRequest> requests)
    {
        foreach (var request in requests)
        {
            var id = Guid.NewGuid().ToString("N");
            var safeName = Path.GetFileName(request.FileName);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "receipt";

            var tempPath = Path.Combine(_tempRoot, $"{id}_{safeName}");
            File.WriteAllBytes(tempPath, request.Content);

            var job = new ReceiptImportJobWorkItem
            {
                Id = id,
                FileName = safeName,
                TempFilePath = tempPath,
                ContentType = request.ContentType,
                AccountId = request.AccountId,
                LedgerEntityId = request.LedgerEntityId,
                AutoAccept = request.AutoAccept
            };

            _jobs[id] = job;
            _ids.Writer.TryWrite(id);
        }

        Prune();
        return GetVisibleJobs();
    }

    public IAsyncEnumerable<string> ReadJobIdsAsync(CancellationToken ct) =>
        _ids.Reader.ReadAllAsync(ct);

    public bool TryGetJob(string id, out ReceiptImportJobWorkItem? job) =>
        _jobs.TryGetValue(id, out job);

    public void MarkProcessing(string id)
    {
        if (_jobs.TryGetValue(id, out var job))
            job.Status = ReceiptImportJobStatus.Processing;
    }

    public void MarkCompleted(string id, string batchId, int lineItemCount, string? warning = null)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return;

        job.Status = ReceiptImportJobStatus.Completed;
        job.ResultBatchId = batchId;
        job.LineItemCount = lineItemCount;
        job.Warning = warning;
        job.CompletedAtUtc = DateTime.UtcNow;
        DeleteTempFile(job.TempFilePath);
        Prune();
    }

    public void MarkFailed(string id, string error)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return;

        job.Status = ReceiptImportJobStatus.Failed;
        job.Error = error;
        job.CompletedAtUtc = DateTime.UtcNow;
        DeleteTempFile(job.TempFilePath);
        Prune();
    }

    public IReadOnlyList<ReceiptImportJobSnapshot> GetVisibleJobs()
    {
        var cutoff = DateTime.UtcNow - VisibleCompletedFor;
        return _jobs.Values
            .Where(j => j.Status is ReceiptImportJobStatus.Queued or ReceiptImportJobStatus.Processing
                || (j.CompletedAtUtc is { } completed && completed >= cutoff))
            .OrderBy(j => j.CreatedAtUtc)
            .Select(ToSnapshot)
            .ToList();
    }

    /// <summary>
    /// Completed jobs stay visible so you can review them after extraction,
    /// but drop off once the resulting import batch is no longer awaiting confirmation.
    /// </summary>
    public static IReadOnlyList<ReceiptImportJobSnapshot> WithoutSavedReceipts(
        IReadOnlyList<ReceiptImportJobSnapshot> jobs,
        IReadOnlySet<string> batchesAwaitingReview)
    {
        return jobs
            .Where(job => job.Status switch
            {
                ReceiptImportJobStatus.Queued or ReceiptImportJobStatus.Processing
                    or ReceiptImportJobStatus.Failed => true,
                ReceiptImportJobStatus.Completed =>
                    job.ResultBatchId is not null
                    && batchesAwaitingReview.Contains(job.ResultBatchId),
                _ => false
            })
            .ToList();
    }

    public bool HasActiveJobs =>
        _jobs.Values.Any(j => j.Status is ReceiptImportJobStatus.Queued or ReceiptImportJobStatus.Processing);

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - VisibleCompletedFor;
        foreach (var job in _jobs.Values)
        {
            if (job.Status is ReceiptImportJobStatus.Queued or ReceiptImportJobStatus.Processing)
                continue;
            if (job.CompletedAtUtc is { } completed && completed >= cutoff)
                continue;

            _jobs.TryRemove(job.Id, out _);
            DeleteTempFile(job.TempFilePath);
        }
    }

    private void ClearOrphanedTempFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_tempRoot))
                File.Delete(file);
        }
        catch (IOException)
        {
            // Best-effort cleanup of leftover images from a previous process.
        }
    }

    private static void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static ReceiptImportJobSnapshot ToSnapshot(ReceiptImportJobWorkItem job) =>
        new(
            job.Id,
            job.FileName,
            job.Status,
            job.Error,
            job.Warning,
            job.ResultBatchId,
            job.LineItemCount,
            job.CreatedAtUtc,
            job.CompletedAtUtc);
}
