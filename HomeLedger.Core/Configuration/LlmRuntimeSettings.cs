namespace HomeLedger.Core.Configuration;

public sealed class LlmRuntimeSettings
{
    public string? VisionModel { get; set; }
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

    public static LlmRuntimeSettings From(LlmSettings settings)
    {
        var runtime = new LlmRuntimeSettings
        {
            VisionModel = string.IsNullOrWhiteSpace(settings.VisionModel)
                ? settings.ResolvedVisionModel
                : settings.VisionModel,
            MaxPdfPages = settings.ResolvedMaxPdfPages,
            MaxReceiptImages = settings.ResolvedMaxReceiptImages,
            MaxReceiptImageEdgePixels = settings.ResolvedMaxReceiptImageEdgePixels,
            FallbackMaxEdgePixels = settings.ResolvedFallbackMaxEdgePixels,
            MaxTallReceiptEdgePixels = settings.ResolvedMaxTallReceiptEdgePixels,
            MinReadableShortEdgePixels = settings.ResolvedMinReadableShortEdgePixels,
            MaxVisionPatches = settings.ResolvedMaxVisionPatches,
            CropReceiptBackground = settings.CropReceiptBackground,
            SplitTallReceipts = settings.SplitTallReceipts,
            ReceiptSplitMinHeightPixels = settings.ResolvedReceiptSplitMinHeightPixels,
            ReceiptSplitOverlapPixels = settings.ResolvedReceiptSplitOverlapPixels,
            NumCtx = settings.ResolvedNumCtx,
            VisionMaxTokens = settings.ResolvedVisionMaxTokens
        };
        runtime.Normalize();
        return runtime;
    }

    public void Normalize()
    {
        VisionModel = string.IsNullOrWhiteSpace(VisionModel) ? null : VisionModel.Trim();
        MaxPdfPages = LlmSettingChoices.SnapOrDefault(MaxPdfPages, LlmSettingChoices.MaxPdfPages, 30);
        MaxReceiptImages = LlmSettingChoices.SnapOrDefault(MaxReceiptImages, LlmSettingChoices.MaxReceiptImages, 20);
        MaxReceiptImageEdgePixels = LlmSettingChoices.SnapMaxEdge(MaxReceiptImageEdgePixels);
        FallbackMaxEdgePixels = LlmSettingChoices.SnapFallback(FallbackMaxEdgePixels, MaxReceiptImageEdgePixels);
        MaxTallReceiptEdgePixels = LlmSettingChoices.SnapTallEdge(MaxTallReceiptEdgePixels, MaxReceiptImageEdgePixels);
        MinReadableShortEdgePixels = LlmSettingChoices.SnapOrDefault(
            MinReadableShortEdgePixels,
            LlmSettingChoices.MinReadableShortEdgePixels,
            616);
        MaxVisionPatches = LlmSettingChoices.SnapOrDefault(MaxVisionPatches, LlmSettingChoices.MaxVisionPatches, 2304);
        ReceiptSplitMinHeightPixels = LlmSettingChoices.SnapOrDefault(
            ReceiptSplitMinHeightPixels,
            LlmSettingChoices.ReceiptSplitMinHeightPixels,
            1400);
        ReceiptSplitOverlapPixels = LlmSettingChoices.SnapOrDefault(
            ReceiptSplitOverlapPixels,
            LlmSettingChoices.ReceiptSplitOverlapPixels,
            224);
        NumCtx = LlmSettingChoices.SnapNumCtx(NumCtx);
        VisionMaxTokens = LlmSettingChoices.SnapOrDefault(VisionMaxTokens, LlmSettingChoices.VisionMaxTokens, 2048);
    }
}
