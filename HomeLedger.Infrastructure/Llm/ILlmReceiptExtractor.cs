namespace HomeLedger.Infrastructure.Llm;

public record ExtractedReceiptLine(
    DateOnly Date,
    decimal Amount,
    string Description,
    string? SuggestedCategoryName,
    decimal? Quantity = null,
    string? QuantityUnit = null,
    decimal? UnitPrice = null);

public record ExtractedReceipt(
    string Merchant,
    DateOnly? ReceiptDate,
    string? ExternalId,
    IReadOnlyList<ExtractedReceiptLine> LineItems);

public enum ReceiptVisionSlice
{
    Full,
    Top,
    Bottom
}

public interface ILlmReceiptExtractor
{
    bool IsEnabled { get; }
    Task<ExtractedReceipt?> ExtractReceiptAsync(
        StatementPageImage image,
        IReadOnlyList<string> categoryNames,
        string? sourceFileName = null,
        CancellationToken ct = default,
        ReceiptVisionSlice slice = ReceiptVisionSlice.Full);
}

public class NullLlmReceiptExtractor : ILlmReceiptExtractor
{
    public bool IsEnabled => false;

    public Task<ExtractedReceipt?> ExtractReceiptAsync(
        StatementPageImage image,
        IReadOnlyList<string> categoryNames,
        string? sourceFileName = null,
        CancellationToken ct = default,
        ReceiptVisionSlice slice = ReceiptVisionSlice.Full) =>
        Task.FromResult<ExtractedReceipt?>(null);
}
