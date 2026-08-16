using System.Globalization;
using HomeLedger.Core.Configuration;
using HomeLedger.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Llm;

public class LlmReceiptExtractor : ILlmReceiptExtractor
{
    private const decimal AmountTolerance = 0.01m;

    private const string ExtractionPromptTemplate = """
        Extract every purchasable line item from this receipt image.

        Return JSON only in this exact shape:
        {"merchant":"Store Name","receipt_date":"yyyy/MM/dd","external_id":null,"line_items":[{"description":"item name","amount":-4.99,"quantity":1,"unit_price":null,"category":"Category Name"}]}

        Rules:
        - merchant is the store or business name from the receipt header (e.g. Walmart, Costco, Shell).
        - Include every product/service line with its own amount when visible. Do not collapse to a single total unless the receipt only shows a total.
        - amount is the line/extended total (usually the right-hand column), never the unit price.
        - When a line shows quantity and unit price (e.g. "5 x KENDAMIL INF F 56.99" with 284.95 on the right), amount is -284.95 (5 × 56.99), not -56.99. Keep it as one line item; do not repeat the item quantity times.
        - quantity is the printed item count. Use 1 when none is shown.
        - unit_price is the per-item price when quantity is greater than 1; otherwise null.
        - Put the quantity in description when greater than 1 (e.g. "5x KENDAMIL INF F").
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
            if (string.IsNullOrWhiteSpace(row.Description))
                continue;

            var quantity = row.Quantity is > 0 ? row.Quantity.Value : 1m;
            var amount = ResolveLineAmount(row.Amount, row.UnitPrice, quantity);
            if (amount is null)
                continue;

            var lineDate = receiptDate ?? default;
            if (!receiptDate.HasValue && !TryParseDate(row.Date, out lineDate))
                lineDate = DateOnly.FromDateTime(DateTime.Today);

            lines.Add(new ExtractedReceiptLine(
                lineDate,
                amount.Value,
                FormatDescription(row.Description, quantity),
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

    private static decimal? ResolveLineAmount(decimal? amount, decimal? unitPrice, decimal quantity)
    {
        if (quantity <= 0)
            quantity = 1m;

        var computed = unitPrice is null
            ? (decimal?)null
            : Math.Round(unitPrice.Value * quantity, 2, MidpointRounding.AwayFromZero);

        if (amount is null)
            return PreferExpenseSign(computed, amount, unitPrice);

        if (computed is null || quantity == 1m)
            return amount;

        if (ValuesMatch(amount.Value, computed.Value))
            return PreferExpenseSign(amount.Value, amount, unitPrice);

        // "5 x ITEM 56.99" with 284.95 on the right: models often return the unit price as amount.
        if (unitPrice is not null && ValuesMatch(amount.Value, unitPrice.Value))
            return PreferExpenseSign(computed, amount, unitPrice);

        return amount;
    }

    private static string FormatDescription(string description, decimal quantity)
    {
        description = description.Trim();
        if (quantity <= 1m)
            return description;

        var qtyLabel = FormatQuantity(quantity);
        if (HasLeadingQuantity(description, qtyLabel))
            return description;

        return $"{qtyLabel}x {description}";
    }

    private static bool ValuesMatch(decimal left, decimal right) =>
        Math.Abs(Math.Abs(left) - Math.Abs(right)) <= AmountTolerance;

    private static decimal? PreferExpenseSign(decimal? value, decimal? amount, decimal? unitPrice)
    {
        if (value is null)
            return null;

        if (value.Value > 0 && (amount is < 0 || unitPrice is < 0))
            return -value.Value;

        return value;
    }

    private static string FormatQuantity(decimal quantity) =>
        quantity == decimal.Truncate(quantity)
            ? decimal.Truncate(quantity).ToString(CultureInfo.InvariantCulture)
            : quantity.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool HasLeadingQuantity(string description, string qtyLabel)
    {
        string[] prefixes =
        [
            $"{qtyLabel}x",
            $"{qtyLabel} x",
            $"{qtyLabel}×",
            $"{qtyLabel} ×"
        ];

        foreach (var prefix in prefixes)
        {
            if (description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
    }
}
