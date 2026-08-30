namespace HomeLedger.Core.Configuration;

public sealed class LlmRuntimeSettings
{
    public string? VisionModel { get; set; }
    public int MaxPdfPages { get; set; } = 30;
    public int MaxReceiptImages { get; set; } = 20;
    public int MaxReceiptImageEdgePixels { get; set; } = 1536;
    public bool CropReceiptBackground { get; set; } = true;
    public bool SplitTallReceipts { get; set; } = true;
    public int NumCtx { get; set; }
    public int VisionMaxTokens { get; set; } = 2048;

    public static LlmRuntimeSettings From(LlmSettings settings) => new()
    {
        VisionModel = string.IsNullOrWhiteSpace(settings.VisionModel)
            ? settings.ResolvedVisionModel
            : settings.VisionModel,
        MaxPdfPages = settings.MaxPdfPages,
        MaxReceiptImages = settings.MaxReceiptImages,
        MaxReceiptImageEdgePixels = settings.MaxReceiptImageEdgePixels,
        CropReceiptBackground = settings.CropReceiptBackground,
        SplitTallReceipts = settings.SplitTallReceipts,
        NumCtx = settings.NumCtx,
        VisionMaxTokens = settings.VisionMaxTokens
    };

    public void Normalize()
    {
        VisionModel = string.IsNullOrWhiteSpace(VisionModel) ? null : VisionModel.Trim();
        MaxPdfPages = Math.Clamp(MaxPdfPages, 1, 500);
        MaxReceiptImages = Math.Clamp(MaxReceiptImages, 1, 100);
        if (MaxReceiptImageEdgePixels <= 0)
            MaxReceiptImageEdgePixels = 0;
        else
            MaxReceiptImageEdgePixels = Math.Clamp(MaxReceiptImageEdgePixels, 640, 4096);

        if (NumCtx <= 0)
            NumCtx = 0;
        else
            NumCtx = Math.Clamp(NumCtx, 2048, 32768);

        VisionMaxTokens = VisionMaxTokens <= 0 ? 2048 : Math.Clamp(VisionMaxTokens, 256, 8192);
    }
}
