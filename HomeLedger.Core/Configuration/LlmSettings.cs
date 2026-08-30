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
    public int FallbackMaxEdgePixels { get; set; } = 672;
    public int MaxTallReceiptEdgePixels { get; set; } = 2016;
    public int MinReadableShortEdgePixels { get; set; } = 616;
    public int MaxVisionPatches { get; set; } = 2304;
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

    public int ResolvedMaxReceiptImageEdgePixels =>
        LlmSettingChoices.SnapMaxEdge(MaxReceiptImageEdgePixels);

    public int ResolvedFallbackMaxEdgePixels =>
        LlmSettingChoices.SnapFallback(FallbackMaxEdgePixels, ResolvedMaxReceiptImageEdgePixels);

    public int ResolvedMaxTallReceiptEdgePixels =>
        LlmSettingChoices.SnapTallEdge(MaxTallReceiptEdgePixels, ResolvedMaxReceiptImageEdgePixels);

    public int ResolvedMinReadableShortEdgePixels =>
        LlmSettingChoices.SnapOrDefault(MinReadableShortEdgePixels, LlmSettingChoices.MinReadableShortEdgePixels, 616);

    public int ResolvedMaxVisionPatches =>
        LlmSettingChoices.SnapOrDefault(MaxVisionPatches, LlmSettingChoices.MaxVisionPatches, 2304);

    public int ResolvedReceiptSplitMinHeightPixels =>
        LlmSettingChoices.SnapOrDefault(ReceiptSplitMinHeightPixels, LlmSettingChoices.ReceiptSplitMinHeightPixels, 1400);

    public int ResolvedReceiptSplitOverlapPixels =>
        LlmSettingChoices.SnapOrDefault(ReceiptSplitOverlapPixels, LlmSettingChoices.ReceiptSplitOverlapPixels, 224);

    public int ResolvedMaxReceiptImages =>
        LlmSettingChoices.SnapOrDefault(MaxReceiptImages, LlmSettingChoices.MaxReceiptImages, 20);

    public int ResolvedMaxPdfPages =>
        LlmSettingChoices.SnapOrDefault(MaxPdfPages, LlmSettingChoices.MaxPdfPages, 30);

    public int ResolvedNumCtx =>
        LlmSettingChoices.SnapNumCtx(NumCtx);

    public int ResolvedVisionMaxTokens =>
        LlmSettingChoices.SnapOrDefault(VisionMaxTokens, LlmSettingChoices.VisionMaxTokens, 2048);
}
