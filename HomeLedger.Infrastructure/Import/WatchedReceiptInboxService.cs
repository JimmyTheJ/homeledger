using HomeLedger.Core.Configuration;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Import;

public class WatchedReceiptInboxService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReceiptInboxSettings _settings;
    private readonly ILogger<WatchedReceiptInboxService> _logger;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public WatchedReceiptInboxService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReceiptInboxSettings> settings,
        ILogger<WatchedReceiptInboxService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Receipt inbox watcher is disabled.");
            return;
        }

        if (_settings.AccountId <= 0 || _settings.LedgerEntityId <= 0)
        {
            _logger.LogWarning(
                "Receipt inbox watcher is enabled but AccountId/LedgerEntityId are not configured.");
            return;
        }

        EnsureWatchDirectories();

        _logger.LogInformation("Receipt inbox watcher started for {Path}", ResolveWatchPath());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Receipt inbox scan failed.");
            }

            var delay = TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds));
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        var watchPath = ResolveWatchPath();
        if (!Directory.Exists(watchPath))
        {
            EnsureWatchDirectories();
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(watchPath))
        {
            if (ct.IsCancellationRequested)
                return;

            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            if (_inFlight.Contains(filePath))
                continue;

            _inFlight.Add(filePath);

            try
            {
                await ProcessFileAsync(filePath, ct);
            }
            finally
            {
                _inFlight.Remove(filePath);
            }
        }
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var receipts = scope.ServiceProvider.GetRequiredService<IReceiptImageImportService>();
        var import = scope.ServiceProvider.GetRequiredService<ICsvImportService>();

        var fileName = Path.GetFileName(filePath);
        if (!receipts.IsReceiptImageFile(fileName, null))
            return;

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(filePath, ct);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not read inbox file {FilePath}", filePath);
            return;
        }

        if (content.Length == 0)
            return;

        var (fileSha256, totalSize) = ImportFileFingerprint.HashCombined([content]);
        var priorImport = await import.FindPriorImportAsync(
            fileSha256, totalSize, _settings.AccountId, ct);
        if (priorImport is not null)
        {
            _logger.LogInformation("Skipping already-imported inbox receipt {FileName}", fileName);
            MoveToProcessed(filePath);
            return;
        }

        try
        {
            var extracted = await receipts.ExtractBatchesAsync(
                [new ReceiptImageUpload(fileName, content, null)],
                _settings.LedgerEntityId,
                ct);

            foreach (var batch in extracted)
            {
                await import.CreateBatchFromRowsAsync(
                    batch.Rows,
                    batch.SourceFileName,
                    totalSize,
                    fileSha256,
                    _settings.AccountId,
                    _settings.LedgerEntityId,
                    autoAccept: false,
                    pdfExtractedWithLlm: true,
                    importKind: ImportKind.WatchedReceipt,
                    batchMerchant: batch.Merchant,
                    sourcePath: filePath,
                    ct);

                _logger.LogInformation(
                    "Queued {Count} line item(s) from {Merchant} receipt {FileName} for review",
                    batch.Rows.Count,
                    batch.Merchant,
                    fileName);
            }

            MoveToProcessed(filePath);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Could not extract receipt from inbox file {FileName}", fileName);
            MoveToProcessed(filePath, failed: true);
        }
    }

    private void EnsureWatchDirectories()
    {
        var watchPath = ResolveWatchPath();
        Directory.CreateDirectory(watchPath);

        if (_settings.MoveToProcessed)
        {
            var processedPath = Path.Combine(watchPath, _settings.ProcessedFolderName);
            Directory.CreateDirectory(processedPath);
            Directory.CreateDirectory(Path.Combine(processedPath, "failed"));
        }
    }

    private string ResolveWatchPath()
    {
        if (Path.IsPathRooted(_settings.WatchPath))
            return _settings.WatchPath;

        return Path.GetFullPath(_settings.WatchPath);
    }

    private void MoveToProcessed(string filePath, bool failed = false)
    {
        if (!_settings.MoveToProcessed)
            return;

        try
        {
            var watchPath = ResolveWatchPath();
            var processedRoot = Path.Combine(watchPath, _settings.ProcessedFolderName);
            var targetDir = failed ? Path.Combine(processedRoot, "failed") : processedRoot;
            Directory.CreateDirectory(targetDir);

            var destination = Path.Combine(targetDir, Path.GetFileName(filePath));
            if (File.Exists(destination))
                destination = Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(filePath)}");

            File.Move(filePath, destination);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not move processed inbox file {FilePath}", filePath);
        }
    }
}
