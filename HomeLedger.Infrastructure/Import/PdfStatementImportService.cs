using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;

namespace HomeLedger.Infrastructure.Import;

public interface IPdfStatementImportService
{
    bool IsPdfFile(string fileName, string? contentType);
    Task<IReadOnlyList<CsvImportRow>> ExtractRowsAsync(byte[] pdfContent, CancellationToken ct = default);
}

public class PdfStatementImportService : IPdfStatementImportService
{
    private readonly ILlmStatementExtractor _extractor;
    private readonly IOptionsMonitor<LlmSettings> _settings;
    private readonly ILogger<PdfStatementImportService> _logger;

    public PdfStatementImportService(
        ILlmStatementExtractor extractor,
        IOptionsMonitor<LlmSettings> settings,
        ILogger<PdfStatementImportService> logger)
    {
        _extractor = extractor;
        _settings = settings;
        _logger = logger;
    }

    public bool IsPdfFile(string fileName, string? contentType) =>
        fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<CsvImportRow>> ExtractRowsAsync(byte[] pdfContent, CancellationToken ct = default)
    {
        if (!_extractor.IsEnabled)
        {
            throw new InvalidOperationException(
                "PDF statement import requires LLM integration. Enable Llm:Enabled, set Llm:UseForStatementImport, " +
                "and configure an API key (or use a local OpenAI-compatible vision model such as Ollama llava).");
        }

        var pages = await RenderPagesAsync(pdfContent, ct);
        if (pages.Count == 0)
            throw new InvalidOperationException("The PDF did not contain any readable pages.");

        _logger.LogInformation("Sending {PageCount} statement page(s) to LLM for extraction", pages.Count);

        var extracted = await _extractor.ExtractAsync(pages, ct);
        if (extracted.Count == 0)
        {
            throw new InvalidOperationException(
                "The LLM could not extract any transactions from this PDF. Try a clearer statement export, " +
                "a vision-capable model (e.g. GPT-4o, Claude Sonnet, Gemini Flash), or import a CSV instead.");
        }

        return extracted
            .Select(line => new CsvImportRow(line.Date, line.Amount, line.Description, line.ExternalId))
            .ToList();
    }

    private async Task<IReadOnlyList<StatementPageImage>> RenderPagesAsync(byte[] pdfContent, CancellationToken ct)
    {
        var pages = new List<StatementPageImage>();
        var pageNumber = 0;

        await foreach (var bitmap in Conversion.ToImagesAsync(pdfContent, options: new(Dpi: 200)).WithCancellation(ct))
        {
            pageNumber++;
            if (pageNumber > _settings.CurrentValue.MaxPdfPages)
            {
                _logger.LogWarning("PDF has more than {MaxPages} pages; only the first {MaxPages} were processed",
                    _settings.CurrentValue.MaxPdfPages, _settings.CurrentValue.MaxPdfPages);
                break;
            }

            using (bitmap)
            {
                var prepared = ReceiptImagePreprocessor.PrepareFromBitmap(
                    bitmap,
                    _settings.CurrentValue.ResolvedMaxReceiptImageEdgePixels,
                    cropBackground: false);
                pages.Add(new StatementPageImage(
                    pageNumber,
                    prepared.Content.Length > 0 ? prepared.Content : EncodePng(bitmap),
                    prepared.Content.Length > 0 ? prepared.MimeType : "image/png"));
            }
        }

        return pages;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        return encoded?.ToArray() ?? [];
    }
}
