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
        {"merchant":"Store Name","receipt_date":"yyyy/MM/dd","external_id":null,"is_refund":false,"line_items":[{"description":"item name","amount":-4.99,"quantity":1,"quantity_unit":"ea","unit_price":4.99,"category":"Category Name","is_return":false}]}

        Rules:
        - merchant is the store or business name from the receipt header (e.g. Walmart, Costco, Shell).
        - Include every product/service line with its own amount when visible. Do not collapse to a single total unless the receipt only shows a total.
        - description is the product name only. Do not prefix quantity (not "5x Infant Formula").
        - is_refund is true when the whole receipt is a refund/return (Trans Type REFUND, "** Refunded", RETURN, negative item count). False for ordinary purchases (TYPE PURCHASE).
        - is_return is true for a returned/refunded line, including every line on a refund receipt. False for purchased items.
        - amount is the line/extended total (usually the right-hand column), never the unit price. Use ledger signs, not the printed sign: negative for purchases/expenses, positive for returns/refunds.
        - Many refund receipts print lines as -$7.99; still emit amount 7.99 with is_return true. Many purchase receipts print $12.75 with no minus; still emit amount -12.75 with is_return false.
        - When a line shows quantity and unit price (e.g. "5 x KENDAMIL INF F 56.99" with 284.95 on the right), amount is -284.95 (5 × 56.99), not -56.99. Keep it as one line item; do not repeat the item quantity times.
        - quantity is the printed count or weight and must be positive. Use 1 for ordinary counted items when no quantity is printed. If a refund prints item count -1, quantity is 1 and is_return is true.
        - quantity_unit must be one of: ea, g, kg, oz, lb. Use ea for counted items. Use the printed weight unit for produce (e.g. "0.640 kg @ 1.74/kg" → quantity 0.640, quantity_unit kg, unit_price 1.74, amount -1.11).
        - Do not treat package size in the product name as quantity (a 500g yogurt is still quantity 1, unit ea).
        - unit_price is the positive per-unit price as printed (56.99 each, 1.74/kg). For a single counted item it equals the unsigned line total.
        - category must be exactly one name from this list (best match): {0}
        - If no category fits well, pick the closest match from the list.
        - receipt_date uses yyyy/MM/dd. Infer the year when only month/day is shown.
        - external_id is a receipt/transaction number when visible, otherwise null.
        - Skip subtotals, tax-only lines, payment method lines, and change due unless they are the only amount shown.
        - Do not invent line items that are not visible in the image.
        """;

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<LlmSettings> _settings;
    private readonly ILogger<LlmReceiptExtractor> _logger;

    public LlmReceiptExtractor(HttpClient http, IOptionsMonitor<LlmSettings> settings, ILogger<LlmReceiptExtractor> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled => _settings.CurrentValue.IsReceiptImportEffective();

    public async Task<ExtractedReceipt?> ExtractReceiptAsync(
        StatementPageImage image,
        IReadOnlyList<string> categoryNames,
        string? sourceFileName = null,
        CancellationToken ct = default,
        ReceiptVisionSlice slice = ReceiptVisionSlice.Full)
    {
        if (categoryNames.Count == 0)
            throw new InvalidOperationException("No categories are configured for receipt extraction.");

        var categoryList = string.Join(", ", categoryNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        // string.Format would treat the JSON example braces as format items and throw FormatException.
        var prompt = ExtractionPromptTemplate.Replace("{0}", categoryList, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(sourceFileName))
            prompt += $"\n- Source file name (context only): {sourceFileName}";
        prompt += SliceInstructions(slice);

        var responseText = await LlmVisionHelper.CompleteAsync(_http, _settings.CurrentValue, prompt, [image], _logger, ct);
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

    private static string SliceInstructions(ReceiptVisionSlice slice) => slice switch
    {
        ReceiptVisionSlice.Top => """

            This image is the TOP portion of a long receipt and overlaps the lower half.
            Extract every purchasable line you can see. Merchant, date, and receipt number are usually on this portion.
            """,
        ReceiptVisionSlice.Bottom => """

            This image is the BOTTOM portion of a long receipt and overlaps the upper half.
            Merchant, date, and receipt number may be missing; do not invent them.
            Extract every purchasable line you can see.
            """,
        _ => ""
    };

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

            var isReturn = IsReturnLine(parsed, row);
            var quantity = NormalizeQuantity(row.Quantity);
            var amount = ResolveLineAmount(row.Amount, row.UnitPrice, quantity ?? 1m, isReturn);
            if (amount is null)
                continue;

            var quantityUnit = QuantityUnits.Normalize(row.QuantityUnit);
            if (quantity is not null && quantityUnit is null && quantity == decimal.Truncate(quantity.Value))
                quantityUnit = QuantityUnits.Each;

            var unitPrice = row.UnitPrice is null || row.UnitPrice == 0
                ? (decimal?)null
                : Math.Abs(row.UnitPrice.Value);
            if (unitPrice is null && quantity is > 0)
                unitPrice = Math.Round(Math.Abs(amount.Value) / quantity.Value, 4, MidpointRounding.AwayFromZero);

            var lineDate = receiptDate ?? default;
            if (!receiptDate.HasValue && !TryParseDate(row.Date, out lineDate))
                lineDate = DateOnly.FromDateTime(DateTime.Today);

            lines.Add(new ExtractedReceiptLine(
                lineDate,
                amount.Value,
                QuantityUnits.StripLeadingQuantity(row.Description, quantity),
                string.IsNullOrWhiteSpace(row.Category) ? null : row.Category.Trim(),
                quantity,
                quantityUnit,
                unitPrice));
        }

        if (lines.Count == 0)
            return null;

        return new ExtractedReceipt(
            merchant,
            receiptDate ?? lines[0].Date,
            string.IsNullOrWhiteSpace(parsed.ExternalId) ? null : parsed.ExternalId.Trim(),
            lines);
    }

    private static decimal? ResolveLineAmount(decimal? amount, decimal? unitPrice, decimal quantity, bool isReturn)
    {
        if (quantity <= 0)
            quantity = 1m;

        var unit = unitPrice is null ? (decimal?)null : Math.Abs(unitPrice.Value);
        var computed = unit is null
            ? (decimal?)null
            : Math.Round(unit.Value * quantity, 2, MidpointRounding.AwayFromZero);

        decimal? magnitude;
        if (amount is null)
            magnitude = computed;
        else if (computed is null || quantity == 1m)
            magnitude = Math.Abs(amount.Value);
        else if (ValuesMatch(amount.Value, computed.Value))
            magnitude = computed;
        else if (unit is not null && ValuesMatch(amount.Value, unit.Value))
            magnitude = computed;
        else
            magnitude = Math.Abs(amount.Value);

        if (magnitude is null)
            return null;

        return ApplyLedgerSign(magnitude.Value, isReturn);
    }

    private static bool ValuesMatch(decimal left, decimal right) =>
        Math.Abs(Math.Abs(left) - Math.Abs(right)) <= AmountTolerance;

    private static decimal ApplyLedgerSign(decimal magnitude, bool isReturn)
    {
        var abs = Math.Abs(magnitude);
        return isReturn ? abs : -abs;
    }

    private static bool IsReturnLine(ReceiptExtractionResponse receipt, ReceiptExtractionLine row)
    {
        if (row.IsReturn == true || row.IsRefund == true)
            return true;

        if (row.IsReturn == false)
            return false;

        return receipt.IsRefund == true || row.Quantity is < 0;
    }

    private static decimal? NormalizeQuantity(decimal? quantity)
    {
        if (quantity is null || quantity.Value == 0)
            return null;

        return Math.Abs(quantity.Value);
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
        public bool? IsRefund { get; set; }
        public List<ReceiptExtractionLine> LineItems { get; set; } = [];
    }

    private sealed class ReceiptExtractionLine
    {
        public string? Date { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Quantity { get; set; }
        public string? QuantityUnit { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public bool? IsReturn { get; set; }
        public bool? IsRefund { get; set; }
    }
}
