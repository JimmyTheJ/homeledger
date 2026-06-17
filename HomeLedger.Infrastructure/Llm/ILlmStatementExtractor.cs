namespace HomeLedger.Infrastructure.Llm;

public record StatementPageImage(int PageNumber, byte[] PngBytes, string MimeType = "image/png");

public record ExtractedStatementLine(
    DateOnly Date,
    decimal Amount,
    string Description,
    string? ExternalId);

public interface ILlmStatementExtractor
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<ExtractedStatementLine>> ExtractAsync(
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct = default);
}

public class NullLlmStatementExtractor : ILlmStatementExtractor
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<ExtractedStatementLine>> ExtractAsync(
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExtractedStatementLine>>([]);
}
