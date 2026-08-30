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
    public int MaxReceiptImageEdgePixels { get; set; } = 1536;
    public bool CropReceiptBackground { get; set; } = true;
    public bool SplitTallReceipts { get; set; } = true;
    public int ReceiptSplitMinHeightPixels { get; set; } = 1400;
    public int ReceiptSplitOverlapPixels { get; set; } = 224;
    public int NumCtx { get; set; }
    public int VisionMaxTokens { get; set; } = 2048;

    public LlmProvider ResolvedProvider => LlmProviderDefaults.Parse(Provider);

    public string ResolvedVisionModel =>
        string.IsNullOrWhiteSpace(VisionModel)
            ? LlmProviderDefaults.VisionModel(ResolvedProvider)
            : VisionModel;

    public int ResolvedMaxReceiptImageEdgePixels
    {
        get
        {
            if (MaxReceiptImageEdgePixels <= 0)
                return 0;

            return Math.Clamp(MaxReceiptImageEdgePixels, 640, 4096);
        }
    }

    public int ResolvedReceiptSplitMinHeightPixels =>
        ReceiptSplitMinHeightPixels <= 0
            ? 1400
            : Math.Clamp(ReceiptSplitMinHeightPixels, 800, 4096);

    public int ResolvedReceiptSplitOverlapPixels =>
        ReceiptSplitOverlapPixels <= 0
            ? 224
            : Math.Clamp(ReceiptSplitOverlapPixels, 56, 560);

    public int ResolvedNumCtx =>
        NumCtx <= 0 ? 0 : Math.Clamp(NumCtx, 2048, 32768);

    public int ResolvedVisionMaxTokens =>
        VisionMaxTokens <= 0 ? 2048 : Math.Clamp(VisionMaxTokens, 256, 8192);
}
