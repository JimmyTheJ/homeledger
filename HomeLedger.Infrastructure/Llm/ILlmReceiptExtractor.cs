namespace HomeLedger.Infrastructure.Llm;

public record ExtractedReceiptLine(
    DateOnly Date,
    decimal Amount,
    string Description,
    string? SuggestedCategoryName);

public record ExtractedReceipt(
    string Merchant,
    DateOnly? ReceiptDate,
    string? ExternalId,
    IReadOnlyList<ExtractedReceiptLine> LineItems);

public interface ILlmReceiptExtractor
{
    bool IsEnabled { get; }
    Task<ExtractedReceipt?> ExtractReceiptAsync(
        StatementPageImage image,
        IReadOnlyList<string> categoryNames,
        string? sourceFileName = null,
        CancellationToken ct = default);
}

public class NullLlmReceiptExtractor : ILlmReceiptExtractor
{
    public bool IsEnabled => false;

    public Task<ExtractedReceipt?> ExtractReceiptAsync(
        StatementPageImage image,
        IReadOnlyList<string> categoryNames,
        string? sourceFileName = null,
        CancellationToken ct = default) =>
        Task.FromResult<ExtractedReceipt?>(null);
}
