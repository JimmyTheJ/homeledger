using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Import;

public record ReceiptImageUpload(string FileName, byte[] Content, string? ContentType);

public interface IReceiptImageImportService
{
    bool IsReceiptImageFile(string fileName, string? contentType);
    Task<IReadOnlyList<CsvImportRow>> ExtractRowsAsync(
        IReadOnlyList<ReceiptImageUpload> images,
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
    private readonly LlmSettings _settings;
    private readonly ILogger<ReceiptImageImportService> _logger;

    public ReceiptImageImportService(
        ILlmReceiptExtractor extractor,
        IOptions<LlmSettings> settings,
        ILogger<ReceiptImageImportService> logger)
    {
        _extractor = extractor;
        _settings = settings.Value;
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

    public async Task<IReadOnlyList<CsvImportRow>> ExtractRowsAsync(
        IReadOnlyList<ReceiptImageUpload> images,
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

        if (images.Count > _settings.MaxReceiptImages)
        {
            throw new InvalidOperationException(
                $"Too many receipt images ({images.Count}). Maximum allowed is {_settings.MaxReceiptImages}. " +
                "Upload fewer images or raise Llm:MaxReceiptImages.");
        }

        var rows = new List<CsvImportRow>();
        var pageNumber = 0;

        foreach (var image in images)
        {
            pageNumber++;
            if (!IsReceiptImageFile(image.FileName, image.ContentType))
            {
                throw new InvalidOperationException(
                    $"Unsupported file type: {image.FileName}. Use JPEG, PNG, WebP, GIF, BMP, or HEIC.");
            }

            var mimeType = ResolveMimeType(image.FileName, image.ContentType);
            var page = new StatementPageImage(pageNumber, image.Content, mimeType);

            _logger.LogInformation("Sending receipt image {FileName} to LLM for extraction", image.FileName);

            var extracted = await _extractor.ExtractAsync(page, image.FileName, ct);
            if (extracted.Count == 0)
            {
                _logger.LogWarning("No transactions extracted from receipt image {FileName}", image.FileName);
                continue;
            }

            rows.AddRange(extracted.Select(line =>
                new CsvImportRow(line.Date, line.Amount, line.Description, line.ExternalId)));
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "The LLM could not extract any transactions from the uploaded receipt images. " +
                "Try clearer photos, a vision-capable model (e.g. GPT-4o, Claude Sonnet, Gemini Flash, Ollama qwen2.5vl), " +
                "or enter transactions manually.");
        }

        return rows;
    }

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
