namespace HomeLedger.Core.Configuration;

public class LlmSettings
{
    public const string SectionName = "Llm";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = nameof(LlmProvider.OpenAiCompatible);
    public string BaseUrl { get; set; } = LlmProviderDefaults.BaseUrl(LlmProvider.OpenAiCompatible);
    public string? ApiKey { get; set; }
    public string DefaultModel { get; set; } = LlmProviderDefaults.TextModel(LlmProvider.OpenAiCompatible);
    public string? VisionModel { get; set; }
    public bool UseForCategorization { get; set; } = true;
    public bool UseForNotesCleanup { get; set; }
    public bool UseForStatementImport { get; set; } = true;
    public bool UseForReceiptImport { get; set; } = true;
    public bool UseForImportClassification { get; set; } = true;
    public int MaxPdfPages { get; set; } = 30;
    public int MaxReceiptImages { get; set; } = 20;

    public LlmProvider ResolvedProvider => LlmProviderDefaults.Parse(Provider);

    public string ResolvedVisionModel =>
        string.IsNullOrWhiteSpace(VisionModel)
            ? LlmProviderDefaults.VisionModel(ResolvedProvider)
            : VisionModel;
}
