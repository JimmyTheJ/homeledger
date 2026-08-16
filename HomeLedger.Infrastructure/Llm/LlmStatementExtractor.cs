using System.Globalization;
using HomeLedger.Core.Configuration;
using HomeLedger.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Llm;

public class LlmStatementExtractor : ILlmStatementExtractor
{
    private const string ExtractionPrompt = """
        Extract every individual transaction line from these bank or credit card statement page images.

        Return JSON only in this exact shape:
        {"transactions":[{"date":"yyyy/MM/dd","amount":-12.34,"description":"merchant or memo","external_id":null}]}

        Rules:
        - Include purchases, payments, deposits, fees, refunds, and transfers shown as line items.
        - amount must be negative for debits/charges/payments out and positive for credits/deposits/payments in.
        - Use yyyy/MM/dd for dates. Infer the statement year when only month/day is shown.
        - description should be the merchant, payee, or memo text for the line.
        - external_id should be a reference/check/confirmation number when visible, otherwise null.
        - Skip headers, footers, section titles, running balances, opening/closing balances, and summary totals.
        - Do not invent transactions that are not visible in the images.
        - Combine multi-page duplicates only once.
        """;

    private readonly HttpClient _http;
    private readonly LlmSettings _settings;
    private readonly ILogger<LlmStatementExtractor> _logger;

    public LlmStatementExtractor(HttpClient http, IOptions<LlmSettings> settings, ILogger<LlmStatementExtractor> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsEnabled => _settings.IsStatementImportEffective();

    public async Task<IReadOnlyList<ExtractedStatementLine>> ExtractAsync(
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct = default)
    {
        if (pages.Count == 0)
            return [];

        var responseText = await LlmVisionHelper.CompleteAsync(_http, _settings, ExtractionPrompt, pages, _logger, ct);

        var parsed = LlmJson.Deserialize<StatementExtractionResponse>(responseText);
        if (parsed?.Transactions is null || parsed.Transactions.Count == 0)
        {
            _logger.LogWarning("LLM returned no transactions. Raw response length: {Length}", responseText?.Length ?? 0);
            return [];
        }

        var lines = new List<ExtractedStatementLine>();
        foreach (var row in parsed.Transactions)
        {
            if (row.Amount is null || string.IsNullOrWhiteSpace(row.Description))
                continue;

            if (!TryParseDate(row.Date, out var date))
                continue;

            lines.Add(new ExtractedStatementLine(
                date,
                row.Amount.Value,
                row.Description.Trim(),
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

    private sealed class StatementExtractionResponse
    {
        public List<StatementExtractionLine> Transactions { get; set; } = [];
    }

    private sealed class StatementExtractionLine
    {
        public string? Date { get; set; }
        public decimal? Amount { get; set; }
        public string? Description { get; set; }
        public string? ExternalId { get; set; }
    }
}
