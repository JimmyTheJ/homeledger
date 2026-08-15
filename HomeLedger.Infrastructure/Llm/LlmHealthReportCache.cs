namespace HomeLedger.Infrastructure.Llm;

public sealed class LlmHealthReportCache
{
    private LlmHealthReport? _report;

    public LlmHealthReport? Get() => Volatile.Read(ref _report);

    public void Set(LlmHealthReport report) => Volatile.Write(ref _report, report);
}
