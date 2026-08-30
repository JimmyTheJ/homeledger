using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Import;

public record ReceiptImageUpload(string FileName, byte[] Content, string? ContentType);

public record ReceiptExtractedBatch(
    string Merchant,
    string SourceFileName,
    IReadOnlyList<CsvImportRow> Rows);

public interface IReceiptImageImportService
{
    bool IsReceiptImageFile(string fileName, string? contentType);
    Task<IReadOnlyList<ReceiptExtractedBatch>> ExtractBatchesAsync(
        IReadOnlyList<ReceiptImageUpload> images,
        int ledgerEntityId,
        CancellationToken ct = default);
}

public class ReceiptImageImportService : IReceiptImageImportService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic", ".heif"
    };

    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/bmp",
        "image/heic",
        "image/heif"
    };

    private readonly ILlmReceiptExtractor _extractor;
    private readonly HomeLedgerDbContext _db;
    private readonly IOptionsMonitor<LlmSettings> _settings;
    private readonly ILogger<ReceiptImageImportService> _logger;

    private LlmSettings Settings => _settings.CurrentValue;

    public ReceiptImageImportService(
        ILlmReceiptExtractor extractor,
        HomeLedgerDbContext db,
        IOptionsMonitor<LlmSettings> settings,
        ILogger<ReceiptImageImportService> logger)
    {
        _extractor = extractor;
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public bool IsReceiptImageFile(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension) && SupportedExtensions.Contains(extension))
            return true;

        return !string.IsNullOrWhiteSpace(contentType)
            && SupportedContentTypes.Contains(contentType.Split(';')[0].Trim());
    }

    public async Task<IReadOnlyList<ReceiptExtractedBatch>> ExtractBatchesAsync(
        IReadOnlyList<ReceiptImageUpload> images,
        int ledgerEntityId,
        CancellationToken ct = default)
    {
        if (!_extractor.IsEnabled)
        {
            throw new InvalidOperationException(
                "Receipt image import requires LLM integration. Enable Llm:Enabled, set Llm:UseForReceiptImport, " +
                "and configure an API key (or use a local OpenAI-compatible vision model such as Ollama qwen2.5vl).");
        }

        if (images.Count == 0)
            throw new InvalidOperationException("Please select at least one receipt image.");

        if (images.Count > Settings.MaxReceiptImages)
        {
            throw new InvalidOperationException(
                $"Too many receipt images ({images.Count}). Maximum allowed is {Settings.MaxReceiptImages}. " +
                "Upload fewer images or raise Llm:MaxReceiptImages.");
        }

        var categoryNames = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive && (c.LedgerEntityId == null || c.LedgerEntityId == ledgerEntityId))
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(ct);

        if (categoryNames.Count == 0)
            throw new InvalidOperationException("Configure at least one active category before importing receipts.");

        var batches = new List<ReceiptExtractedBatch>();
        var pageNumber = 0;

        foreach (var image in images)
        {
            pageNumber++;
            if (!IsReceiptImageFile(image.FileName, image.ContentType))
            {
                throw new InvalidOperationException(
                    $"Unsupported file type: {image.FileName}. Use JPEG, PNG, WebP, GIF, BMP, or HEIC.");
            }

            if (!ReceiptImageContentValidator.LooksLikeSupportedImage(image.Content, image.FileName))
            {
                throw new InvalidOperationException(
                    $"File content does not match a supported receipt image format: {image.FileName}.");
            }

            var mimeType = ResolveMimeType(image.FileName, image.ContentType);
            var prepared = PrepareForVision(image, mimeType, Settings.ResolvedMaxReceiptImageEdgePixels);

            _logger.LogInformation("Sending receipt image {FileName} to LLM for extraction", image.FileName);

            var extracted = await ExtractPreparedAsync(
                prepared,
                categoryNames,
                image.FileName,
                pageNumber,
                ct);
            if (extracted is null || extracted.LineItems.Count == 0)
            {
                _logger.LogWarning("No line items extracted from receipt image {FileName}", image.FileName);
                continue;
            }

            var rows = extracted.LineItems.Select(line => new CsvImportRow(
                line.Date,
                line.Amount,
                line.Description,
                extracted.ExternalId,
                extracted.Merchant,
                line.SuggestedCategoryName,
                image.FileName,
                line.Quantity,
                line.QuantityUnit,
                line.UnitPrice)).ToList();

            batches.Add(new ReceiptExtractedBatch(extracted.Merchant, image.FileName, rows));
        }

        if (batches.Count == 0)
        {
            throw new InvalidOperationException(
                "The LLM could not extract any line items from the uploaded receipt images. " +
                "Try clearer photos, a vision-capable model (e.g. GPT-4o, Claude Sonnet, Gemini Flash, Ollama qwen2.5vl), " +
                "or enter transactions manually.");
        }

        return batches;
    }

    private ReceiptVisionImage PrepareForVision(ReceiptImageUpload image, string mimeType, int maxEdgePixels)
    {
        var prepared = ReceiptImagePreprocessor.Prepare(
            image.Content,
            mimeType,
            maxEdgePixels,
            Settings.CropReceiptBackground);
        if (prepared.Transformed)
        {
            _logger.LogInformation(
                "Prepared receipt {FileName} for vision: {SrcBytes} bytes -> {DstBytes} bytes ({Width}x{Height}, cropped: {Cropped}, deskewed: {Deskewed}, contrast: {Contrast})",
                image.FileName,
                image.Content.Length,
                prepared.Content.Length,
                prepared.Width,
                prepared.Height,
                prepared.Cropped,
                prepared.Deskewed,
                prepared.ContrastEnhanced);
        }

        return prepared;
    }

    private async Task<ExtractedReceipt?> ExtractPreparedAsync(
        ReceiptVisionImage prepared,
        IReadOnlyList<string> categoryNames,
        string fileName,
        int pageNumber,
        CancellationToken ct)
    {
        var parts = Settings.SplitTallReceipts
            ? ReceiptImagePreprocessor.SplitTallIfNeeded(
                prepared,
                Settings.ResolvedReceiptSplitMinHeightPixels,
                Settings.ResolvedReceiptSplitOverlapPixels)
            : [prepared];

        if (parts.Count == 1)
            return await ExtractWithRetryAsync(parts[0], categoryNames, fileName, pageNumber, ReceiptVisionSlice.Full, ct);

        _logger.LogInformation(
            "Split tall receipt {FileName} ({Width}x{Height}) into overlapping {TopWidth}x{TopHeight} and {BottomWidth}x{BottomHeight} tiles",
            fileName,
            prepared.Width,
            prepared.Height,
            parts[0].Width,
            parts[0].Height,
            parts[1].Width,
            parts[1].Height);

        var top = await ExtractTileAsync(
            parts[0],
            categoryNames,
            fileName,
            pageNumber,
            ReceiptVisionSlice.Top,
            ct);
        var bottom = await ExtractTileAsync(
            parts[1],
            categoryNames,
            fileName,
            pageNumber,
            ReceiptVisionSlice.Bottom,
            ct);
        var merged = ReceiptSplitMerger.Combine(top.Receipt, bottom.Receipt);
        if (merged is null && top.Error is not null)
            throw top.Error;
        if (merged is null && bottom.Error is not null)
            throw bottom.Error;
        return merged;
    }

    private async Task<(ExtractedReceipt? Receipt, HttpRequestException? Error)> ExtractTileAsync(
        ReceiptVisionImage prepared,
        IReadOnlyList<string> categoryNames,
        string fileName,
        int pageNumber,
        ReceiptVisionSlice slice,
        CancellationToken ct)
    {
        try
        {
            return (await ExtractWithRetryAsync(prepared, categoryNames, fileName, pageNumber, slice, ct), null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Vision tile {Slice} failed for {FileName} at {Width}x{Height}",
                slice,
                fileName,
                prepared.Width,
                prepared.Height);
            return (null, ex);
        }
    }

    private async Task<ExtractedReceipt?> ExtractWithRetryAsync(
        ReceiptVisionImage prepared,
        IReadOnlyList<string> categoryNames,
        string fileName,
        int pageNumber,
        ReceiptVisionSlice slice,
        CancellationToken ct)
    {
        try
        {
            return await ExtractOnceAsync(prepared, categoryNames, fileName, pageNumber, slice, ct);
        }
        catch (HttpRequestException ex) when (ShouldRetryAfterVisionAssert(ex, prepared))
        {
            _logger.LogWarning(
                ex,
                "Vision model aborted on {FileName} at {Width}x{Height}; retrying at {Edge}px",
                fileName,
                prepared.Width,
                prepared.Height,
                ReceiptImagePreprocessor.FallbackMaxEdgePixels);
            var fallback = ReceiptImagePreprocessor.Prepare(
                prepared.Content,
                prepared.MimeType,
                ReceiptImagePreprocessor.FallbackMaxEdgePixels,
                cropBackground: false);
            if (fallback.Transformed)
            {
                _logger.LogInformation(
                    "Prepared receipt {FileName} fallback for vision: {Width}x{Height}",
                    fileName,
                    fallback.Width,
                    fallback.Height);
            }

            return await ExtractOnceAsync(fallback, categoryNames, fileName, pageNumber, slice, ct);
        }
    }

    private Task<ExtractedReceipt?> ExtractOnceAsync(
        ReceiptVisionImage prepared,
        IReadOnlyList<string> categoryNames,
        string fileName,
        int pageNumber,
        ReceiptVisionSlice slice,
        CancellationToken ct)
    {
        var page = new StatementPageImage(pageNumber, prepared.Content, prepared.MimeType);
        return _extractor.ExtractReceiptAsync(page, categoryNames, fileName, ct, slice);
    }

    private static bool ShouldRetryAfterVisionAssert(HttpRequestException ex, ReceiptVisionImage prepared) =>
        LlmVisionHelper.IsModelRunnerAssert(ex)
        && Math.Max(prepared.Width, prepared.Height) > ReceiptImagePreprocessor.FallbackMaxEdgePixels;

    private static string ResolveMimeType(string fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var normalized = contentType.Split(';')[0].Trim();
            if (SupportedContentTypes.Contains(normalized))
                return normalized;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            _ => "image/png"
        };
    }
}
