using System.Globalization;
using HomeLedger.Core.Configuration;
using HomeLedger.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Llm;

public class LlmReceiptExtractor : ILlmReceiptExtractor
{
    private const string ExtractionPrompt = """
        Extract purchase transaction(s) from this receipt image.

        Return JSON only in this exact shape:
        {"transactions":[{"date":"yyyy/MM/dd","amount":-12.34,"description":"merchant or memo","external_id":null}]}

        Rules:
        - Prefer one row for the final total paid/charged. Add separate rows only when the receipt clearly lists distinct purchases you are confident about.
        - amount must be negative for purchases/expenses and positive for refunds/returns.
        - Use yyyy/MM/dd for dates. Infer the year from context when only month/day is visible.
        - description should include the store/merchant name and a brief summary (e.g. "Costco - groceries").
        - external_id should be a receipt/transaction/authorization number when visible, otherwise null.
        - Skip subtotals, tax lines, tips, and payment-method lines unless they are the only charge amount shown.
        - Do not invent transactions that are not visible in the image.
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

    public async Task<IReadOnlyList<ExtractedStatementLine>> ExtractAsync(
        StatementPageImage image,
        string? sourceFileName = null,
        CancellationToken ct = default)
    {
        var prompt = string.IsNullOrWhiteSpace(sourceFileName)
            ? ExtractionPrompt
            : ExtractionPrompt + $"\n- Source file name (for context only): {sourceFileName}";

        var responseText = await LlmVisionHelper.CompleteAsync(_http, _settings, prompt, [image], ct);

        var parsed = LlmJson.Deserialize<ReceiptExtractionResponse>(responseText);
        if (parsed?.Transactions is null || parsed.Transactions.Count == 0)
        {
            _logger.LogWarning(
                "LLM returned no receipt transactions for {FileName}. Raw response length: {Length}",
                sourceFileName ?? "receipt",
                responseText?.Length ?? 0);
            return [];
        }

        var lines = new List<ExtractedStatementLine>();
        foreach (var row in parsed.Transactions)
        {
            if (row.Amount is null || string.IsNullOrWhiteSpace(row.Description))
                continue;

            if (!TryParseDate(row.Date, out var date))
                continue;

            var description = row.Description.Trim();
            if (!string.IsNullOrWhiteSpace(sourceFileName)
                && !description.Contains(sourceFileName, StringComparison.OrdinalIgnoreCase))
            {
                description = $"{description} [{sourceFileName}]";
            }

            lines.Add(new ExtractedStatementLine(
                date,
                row.Amount.Value,
                description,
                string.IsNullOrWhiteSpace(row.ExternalId) ? null : row.ExternalId.Trim()));
        }

        return Deduplicate(lines);
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

    private static IReadOnlyList<ExtractedStatementLine> Deduplicate(IEnumerable<ExtractedStatementLine> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ExtractedStatementLine>();

        foreach (var line in lines)
        {
            var key = !string.IsNullOrWhiteSpace(line.ExternalId)
                ? $"id:{line.ExternalId}"
                : $"row:{line.Date:yyyyMMdd}:{line.Amount}:{line.Description.Trim().ToLowerInvariant()}";

            if (!seen.Add(key))
                continue;

            result.Add(line);
        }

        return result;
    }

    private sealed class ReceiptExtractionResponse
    {
        public List<ReceiptExtractionLine> Transactions { get; set; } = [];
    }

    private sealed class ReceiptExtractionLine
    {
        public string? Date { get; set; }
        public decimal? Amount { get; set; }
        public string? Description { get; set; }
        public string? ExternalId { get; set; }
    }
}
