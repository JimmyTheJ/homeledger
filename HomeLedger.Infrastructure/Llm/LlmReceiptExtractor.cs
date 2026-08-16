using System.Globalization;
using HomeLedger.Core.Configuration;
using HomeLedger.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Llm;

public class LlmReceiptExtractor : ILlmReceiptExtractor
{
    private const string ExtractionPromptTemplate = """
        Extract every purchasable line item from this receipt image.

        Return JSON only in this exact shape:
        {"merchant":"Store Name","receipt_date":"yyyy/MM/dd","external_id":null,"line_items":[{"description":"item name","amount":-4.99,"category":"Category Name"}]}

        Rules:
        - merchant is the store or business name from the receipt header (e.g. Walmart, Costco, Shell).
        - Include every product/service line with its own amount when visible. Do not collapse to a single total unless the receipt only shows a total.
        - amount must be negative for purchases/expenses and positive for returns.
        - category must be exactly one name from this list (best match): {0}
        - If no category fits well, pick the closest match from the list.
        - receipt_date uses yyyy/MM/dd. Infer the year when only month/day is shown.
        - external_id is a receipt/transaction number when visible, otherwise null.
        - Skip subtotals, tax-only lines, payment method lines, and change due unless they are the only amount shown.
        - Do not invent line items that are not visible in the image.
        """;

    private readonly HttpClient _http;
    private readonly LlmSettings _settings;
    private readonly ILogger<LlmReceiptExtractor> _logger;

    public LlmReceiptExtractor(HttpClient http, IOptions<LlmSettings> settings, ILogger<LlmReceiptExtractor> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsEnabled => _settings.IsReceiptImportEffective();

    public async Task<ExtractedReceipt?> ExtractReceiptAsync(
        StatementPageImage image,
        IReadOnlyList<string> categoryNames,
        string? sourceFileName = null,
        CancellationToken ct = default)
    {
        if (categoryNames.Count == 0)
            throw new InvalidOperationException("No categories are configured for receipt extraction.");

        var categoryList = string.Join(", ", categoryNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        // string.Format would treat the JSON example braces as format items and throw FormatException.
        var prompt = ExtractionPromptTemplate.Replace("{0}", categoryList, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(sourceFileName))
            prompt += $"\n- Source file name (context only): {sourceFileName}";

        var responseText = await LlmVisionHelper.CompleteAsync(_http, _settings, prompt, [image], ct);
        var extracted = TryParseResponse(responseText);
        if (extracted is null)
        {
            _logger.LogWarning(
                "LLM returned no usable receipt line items for {FileName}. Raw response: {Preview}",
                sourceFileName ?? "receipt",
                Preview(responseText));
        }

        return extracted;
    }

    internal static ExtractedReceipt? TryParseResponse(string? responseText)
    {
        var parsed = LlmJson.Deserialize<ReceiptExtractionResponse>(responseText);
        if (parsed?.LineItems is null || parsed.LineItems.Count == 0)
            return null;

        var merchant = string.IsNullOrWhiteSpace(parsed.Merchant) ? "Unknown merchant" : parsed.Merchant.Trim();
        var receiptDate = TryParseDate(parsed.ReceiptDate, out var date) ? date : (DateOnly?)null;
        var lines = new List<ExtractedReceiptLine>();

        foreach (var row in parsed.LineItems)
        {
            if (row.Amount is null || string.IsNullOrWhiteSpace(row.Description))
                continue;

            var lineDate = receiptDate ?? default;
            if (!receiptDate.HasValue && !TryParseDate(row.Date, out lineDate))
                lineDate = DateOnly.FromDateTime(DateTime.Today);

            lines.Add(new ExtractedReceiptLine(
                lineDate,
                row.Amount.Value,
                row.Description.Trim(),
                string.IsNullOrWhiteSpace(row.Category) ? null : row.Category.Trim()));
        }

        if (lines.Count == 0)
            return null;

        return new ExtractedReceipt(
            merchant,
            receiptDate ?? lines[0].Date,
            string.IsNullOrWhiteSpace(parsed.ExternalId) ? null : parsed.ExternalId.Trim(),
            lines);
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (HomeLedgerFormats.TryParseDate(value, out date))
            return true;

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string Preview(string? text, int maxChars = 800)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "<empty>";

        var trimmed = text.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "…";
    }

    private sealed class ReceiptExtractionResponse
    {
        public string? Merchant { get; set; }
        public string? ReceiptDate { get; set; }
        public string? ExternalId { get; set; }
        public List<ReceiptExtractionLine> LineItems { get; set; } = [];
    }

    private sealed class ReceiptExtractionLine
    {
        public string? Date { get; set; }
        public decimal? Amount { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
    }
}
