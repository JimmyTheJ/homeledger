using HomeLedger.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Import;

public record ReceiptInboxFileUpload(string FileName, byte[] Content, string? ContentType);

public record ReceiptInboxUploadResult(int SavedCount, IReadOnlyList<string> Rejected);

public interface IReceiptInboxUploadService
{
    bool IsReady { get; }
    string? NotReadyReason { get; }
    Task<ReceiptInboxUploadResult> SaveFilesAsync(
        IReadOnlyList<ReceiptInboxFileUpload> files,
        CancellationToken ct = default);
}

public class ReceiptInboxUploadService : IReceiptInboxUploadService
{
    private const int MaxSafeFileNameLength = 200;
    private const string PartialFileSuffix = ".part";

    private readonly ReceiptInboxSettings _settings;
    private readonly ReceiptInboxPathResolver _paths;
    private readonly IReceiptImageImportService _receipts;
    private readonly ILogger<ReceiptInboxUploadService> _logger;

    public ReceiptInboxUploadService(
        IOptions<ReceiptInboxSettings> settings,
        ReceiptInboxPathResolver paths,
        IReceiptImageImportService receipts,
        ILogger<ReceiptInboxUploadService> logger)
    {
        _settings = settings.Value;
        _paths = paths;
        _receipts = receipts;
        _logger = logger;
    }

    public bool IsReady =>
        _settings.Enabled
        && _settings.AccountId > 0
        && _settings.LedgerEntityId > 0;

    public string? NotReadyReason
    {
        get
        {
            if (!_settings.Enabled)
                return "Receipt inbox is disabled. Enable ReceiptInbox:Enabled in settings.";

            if (_settings.AccountId <= 0 || _settings.LedgerEntityId <= 0)
            {
                return "Receipt inbox account and entity IDs are not configured. Set ReceiptInbox:AccountId and ReceiptInbox:LedgerEntityId.";
            }

            return null;
        }
    }

    public async Task<ReceiptInboxUploadResult> SaveFilesAsync(
        IReadOnlyList<ReceiptInboxFileUpload> files,
        CancellationToken ct = default)
    {
        if (!IsReady)
            throw new InvalidOperationException(NotReadyReason ?? "Receipt inbox is not ready.");

        if (files.Count == 0)
            throw new InvalidOperationException("Please select at least one receipt image.");

        var maxFiles = Math.Max(1, _settings.MaxFilesPerUpload);
        if (files.Count > maxFiles)
        {
            throw new InvalidOperationException(
                $"Too many files ({files.Count}). Maximum allowed per upload is {maxFiles}.");
        }

        var watchPath = _paths.ResolveWatchPath();
        Directory.CreateDirectory(watchPath);

        var rejected = new List<string>();
        var savedCount = 0;

        foreach (var file in files)
        {
            if (file.Content.Length == 0)
            {
                rejected.Add($"{file.FileName}: file is empty.");
                continue;
            }

            if (file.Content.Length > _settings.MaxFileSizeBytes)
            {
                rejected.Add($"{file.FileName}: exceeds maximum size of {_settings.MaxFileSizeBytes / (1024 * 1024)} MB.");
                continue;
            }

            if (!_receipts.IsReceiptImageFile(file.FileName, file.ContentType))
            {
                rejected.Add($"{file.FileName}: unsupported file type.");
                continue;
            }

            if (!ReceiptImageContentValidator.LooksLikeSupportedImage(file.Content, file.FileName))
            {
                rejected.Add($"{file.FileName}: file content does not match a supported receipt image format.");
                continue;
            }

            var safeFileName = SanitizeFileName(file.FileName);
            var destinationPath = ResolveUniqueDestinationPath(watchPath, safeFileName);
            if (!_paths.IsWithinWatchRoot(destinationPath))
            {
                rejected.Add($"{file.FileName}: invalid destination path.");
                continue;
            }

            var partialPath = destinationPath + PartialFileSuffix;
            if (!_paths.IsWithinWatchRoot(partialPath))
            {
                rejected.Add($"{file.FileName}: invalid temporary path.");
                continue;
            }

            try
            {
                await File.WriteAllBytesAsync(partialPath, file.Content, ct);
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                File.Move(partialPath, destinationPath);
                savedCount++;
                _logger.LogInformation("Saved receipt inbox upload {FileName}", Path.GetFileName(destinationPath));
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not save inbox upload {FileName}", file.FileName);
                rejected.Add($"{file.FileName}: could not be saved.");
                TryDelete(partialPath);
            }
        }

        if (savedCount == 0 && rejected.Count > 0)
        {
            throw new InvalidOperationException(
                "No files were saved to the inbox. " + string.Join(" ", rejected));
        }

        return new ReceiptInboxUploadResult(savedCount, rejected);
    }

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
            return $"{Guid.NewGuid():N}.jpg";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(baseName
            .Where(ch => !invalidChars.Contains(ch) && ch != '\0')
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            return $"{Guid.NewGuid():N}.jpg";

        if (sanitized.Length > MaxSafeFileNameLength)
        {
            var extension = Path.GetExtension(sanitized);
            var stem = Path.GetFileNameWithoutExtension(sanitized);
            var maxStemLength = Math.Max(1, MaxSafeFileNameLength - extension.Length);
            sanitized = stem[..Math.Min(stem.Length, maxStemLength)] + extension;
        }

        return sanitized;
    }

    private static string ResolveUniqueDestinationPath(string watchPath, string fileName)
    {
        var destination = Path.Combine(watchPath, fileName);
        if (!File.Exists(destination))
            return destination;

        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(watchPath, $"{stem}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}");
    }

    private static void TryDelete(string path)
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
}
