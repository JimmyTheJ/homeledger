namespace Ledger.Core.Configuration;

public class LlmSettings
{
    public const string SectionName = "Llm";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string? ApiKey { get; set; }
    public string DefaultModel { get; set; } = "llama3.2";
    public bool UseForCategorization { get; set; } = true;
    public bool UseForNotesCleanup { get; set; }
}
