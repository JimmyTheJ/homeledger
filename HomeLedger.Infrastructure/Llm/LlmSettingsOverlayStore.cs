using System.Text.Json;
using System.Text.Json.Serialization;
using HomeLedger.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HomeLedger.Infrastructure.Llm;

public interface ILlmSettingsOverlayStore
{
    string FilePath { get; }
    bool Exists { get; }
    Task SaveAsync(LlmRuntimeSettings values, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class LlmSettingsOverlayStore : ILlmSettingsOverlayStore
{
    public const string FileName = "llm-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConfiguration? _configuration;

    public LlmSettingsOverlayStore(IHostEnvironment environment, IConfiguration configuration)
        : this(Path.Combine(environment.ContentRootPath, "data", FileName), configuration)
    {
    }

    internal LlmSettingsOverlayStore(string filePath, IConfiguration? configuration = null)
    {
        FilePath = filePath;
        _configuration = configuration;
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public async Task SaveAsync(LlmRuntimeSettings values, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        values.Normalize();

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var payload = new OverlayFile
        {
            Llm = new OverlayLlmSection
            {
                VisionModel = values.VisionModel,
                MaxPdfPages = values.MaxPdfPages,
                MaxReceiptImages = values.MaxReceiptImages,
                MaxReceiptImageEdgePixels = values.MaxReceiptImageEdgePixels,
                CropReceiptBackground = values.CropReceiptBackground,
                SplitTallReceipts = values.SplitTallReceipts,
                NumCtx = values.NumCtx,
                VisionMaxTokens = values.VisionMaxTokens
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var tempPath = FilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, FilePath, overwrite: true);
        Reload();
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(FilePath))
            File.Delete(FilePath);

        Reload();
        return Task.CompletedTask;
    }

    private void Reload()
    {
        if (_configuration is IConfigurationRoot root)
            root.Reload();
    }

    private sealed class OverlayFile
    {
        public OverlayLlmSection Llm { get; set; } = new();
    }

    private sealed class OverlayLlmSection
    {
        public string? VisionModel { get; set; }
        public int MaxPdfPages { get; set; }
        public int MaxReceiptImages { get; set; }
        public int MaxReceiptImageEdgePixels { get; set; }
        public bool CropReceiptBackground { get; set; }
        public bool SplitTallReceipts { get; set; }
        public int NumCtx { get; set; }
        public int VisionMaxTokens { get; set; }
    }
}
