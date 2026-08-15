using HomeLedger.Core.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeLedger.Infrastructure.Import;

public class InteractiveReceiptImportProcessor : BackgroundService
{
    private readonly IReceiptImportJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InteractiveReceiptImportProcessor> _logger;

    public InteractiveReceiptImportProcessor(
        IReceiptImportJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<InteractiveReceiptImportProcessor> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.ReadJobIdsAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Receipt import job {JobId} failed unexpectedly", jobId);
                _queue.MarkFailed(jobId, ex.Message);
            }
        }
    }

    private async Task ProcessJobAsync(string jobId, CancellationToken ct)
    {
        if (!_queue.TryGetJob(jobId, out var job) || job is null)
            return;

        _queue.MarkProcessing(jobId);
        _logger.LogInformation("Extracting receipt {FileName} ({JobId})", job.FileName, jobId);

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(job.TempFilePath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _queue.MarkFailed(jobId, $"Could not read uploaded file {job.FileName}.");
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var receipts = scope.ServiceProvider.GetRequiredService<IReceiptImageImportService>();
        var import = scope.ServiceProvider.GetRequiredService<ICsvImportService>();

        try
        {
            var extractedBatches = await receipts.ExtractBatchesAsync(
                [new ReceiptImageUpload(job.FileName, content, job.ContentType)],
                job.LedgerEntityId,
                ct);

            var (fileSha256, totalSize) = ImportFileFingerprint.HashCombined([content]);
            string? warning = null;
            var priorImport = await import.FindPriorImportAsync(fileSha256, totalSize, job.AccountId, ct);
            if (priorImport is not null)
            {
                warning =
                    $"This receipt was already imported on {priorImport.CompletedAt:yyyy/MM/dd}. Duplicate rows will be skipped automatically.";
            }

            ImportBatchCreated? created = null;
            var totalRows = 0;
            foreach (var extracted in extractedBatches)
            {
                var batch = await import.CreateBatchFromRowsAsync(
                    extracted.Rows,
                    extracted.SourceFileName,
                    totalSize,
                    fileSha256,
                    job.AccountId,
                    job.LedgerEntityId,
                    job.AutoAccept,
                    pdfExtractedWithLlm: true,
                    importKind: ImportKind.Receipt,
                    batchMerchant: extracted.Merchant,
                    sourcePath: extracted.SourceFileName,
                    ct: ct);

                created ??= new ImportBatchCreated(batch.Id, extracted.Rows.Count);
                totalRows += extracted.Rows.Count;
            }

            if (created is null)
            {
                _queue.MarkFailed(jobId, "The vision model did not extract any line items from this receipt.");
                return;
            }

            _queue.MarkCompleted(jobId, created.BatchId, totalRows, warning);
            _logger.LogInformation(
                "Extracted {Count} line item(s) from receipt {FileName}",
                totalRows,
                job.FileName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _queue.MarkFailed(jobId, "Receipt extraction was cancelled.");
            throw;
        }
        catch (TaskCanceledException)
        {
            _queue.MarkFailed(
                jobId,
                "The vision model timed out while reading this receipt. Try a smaller image or check that the LLM endpoint is running.");
        }
        catch (HttpRequestException ex)
        {
            _queue.MarkFailed(jobId, $"Could not reach the vision model: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            _queue.MarkFailed(jobId, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Receipt import job {JobId} failed", jobId);
            _queue.MarkFailed(jobId, $"Could not extract this receipt: {ex.Message}");
        }
    }

    private sealed record ImportBatchCreated(string BatchId, int RowCount);
}
