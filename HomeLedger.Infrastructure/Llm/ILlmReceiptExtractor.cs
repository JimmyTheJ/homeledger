namespace HomeLedger.Infrastructure.Llm;

public interface ILlmReceiptExtractor
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<ExtractedStatementLine>> ExtractAsync(
        StatementPageImage image,
        string? sourceFileName = null,
        CancellationToken ct = default);
}

public class NullLlmReceiptExtractor : ILlmReceiptExtractor
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<ExtractedStatementLine>> ExtractAsync(
        StatementPageImage image,
        string? sourceFileName = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExtractedStatementLine>>([]);
}
