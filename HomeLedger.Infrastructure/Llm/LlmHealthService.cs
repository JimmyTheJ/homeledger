using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Import;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Llm;

public record LlmFeatureStatus(
    string Feature,
    bool ConfiguredEnabled,
    bool EffectiveEnabled,
    string? DisabledReason);

public record LlmHealthReport(
    bool GlobalEnabled,
    bool RegisteredAtStartup,
    bool ConnectionOk,
    string? ConnectionMessage,
    string BaseUrl,
    string DefaultModel,
    string VisionModel,
    IReadOnlyList<LlmFeatureStatus> Features,
    DateTime CheckedAtUtc,
    bool ConnectionChecked = false);

public interface ILlmHealthService
{
    LlmHealthReport GetConfigurationStatus();
    Task<LlmHealthReport> CheckHealthAsync(CancellationToken ct = default);
}

public class LlmHealthService : ILlmHealthService
{
    private readonly LlmSettings _settings;
    private readonly ILlmClient _llmClient;
    private readonly IImportRowClassifier _rowClassifier;
    private readonly ILlmStatementExtractor _statementExtractor;
    private readonly ILlmReceiptExtractor _receiptExtractor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmHealthReportCache _cache;
    private readonly ILogger<LlmHealthService> _logger;

    public LlmHealthService(
        IOptions<LlmSettings> settings,
        ILlmClient llmClient,
        IImportRowClassifier rowClassifier,
        ILlmStatementExtractor statementExtractor,
        ILlmReceiptExtractor receiptExtractor,
        IHttpClientFactory httpClientFactory,
        LlmHealthReportCache cache,
        ILogger<LlmHealthService> logger)
    {
        _settings = settings.Value;
        _llmClient = llmClient;
        _rowClassifier = rowClassifier;
        _statementExtractor = statementExtractor;
        _receiptExtractor = receiptExtractor;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public LlmHealthReport GetConfigurationStatus() =>
        _cache.Get() ?? BuildReport(connectionChecked: false, connectionOk: false, "Not checked yet.");

    public async Task<LlmHealthReport> CheckHealthAsync(CancellationToken ct = default)
    {
        LlmHealthReport report;
        if (!_settings.Enabled)
        {
            report = BuildReport(connectionChecked: true, connectionOk: false, "LLM is disabled in configuration.");
        }
        else
        {
            var (connectionOk, connectionMessage) = await ProbeConnectionAsync(ct);
            report = BuildReport(connectionChecked: true, connectionOk, connectionMessage);
        }

        _cache.Set(report);
        return report;
    }

    private LlmHealthReport BuildReport(bool connectionChecked, bool connectionOk, string? connectionMessage)
    {
        var features = new List<LlmFeatureStatus>
        {
            new(
                "Categorization",
                _settings.Enabled && _settings.UseForCategorization,
                _llmClient.IsEnabled,
                _settings.DescribeCategorizationBlocker()),
            new(
                "Import skip classification",
                _settings.Enabled && _settings.UseForImportClassification,
                _rowClassifier.IsEnabled,
                _settings.DescribeImportClassificationBlocker()),
            new(
                "PDF statement import",
                _settings.Enabled && _settings.UseForStatementImport,
                _statementExtractor.IsEnabled,
                _settings.DescribeStatementImportBlocker()),
            new(
                "Receipt image import",
                _settings.Enabled && _settings.UseForReceiptImport,
                _receiptExtractor.IsEnabled,
                _settings.DescribeReceiptImportBlocker())
        };

        // Only a real failed probe should hide otherwise-configured features.
        if (connectionChecked && _settings.Enabled && !connectionOk && connectionMessage is not null)
        {
            features = features.Select(f => f with
            {
                EffectiveEnabled = false,
                DisabledReason = f.EffectiveEnabled
                    ? $"Endpoint unreachable: {connectionMessage}"
                    : f.DisabledReason ?? $"Endpoint unreachable: {connectionMessage}"
            }).ToList();
        }

        return new LlmHealthReport(
            _settings.Enabled,
            RegisteredAtStartup: _settings.Enabled,
            connectionOk,
            connectionMessage,
            _settings.BaseUrl,
            _settings.DefaultModel,
            _settings.ResolvedVisionModel,
            features,
            DateTime.UtcNow,
            connectionChecked);
    }

    private async Task<(bool Ok, string? Message)> ProbeConnectionAsync(CancellationToken ct)
    {
        var probeUri = _settings.ResolveHealthProbeUri();
        if (probeUri is null)
            return (false, "Base URL is missing or invalid.");

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(LlmHealthService));
            client.Timeout = TimeSpan.FromSeconds(8);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            using var response = await client.GetAsync(probeUri, ct);
            if (response.IsSuccessStatusCode)
                return (true, "Connected.");

            var body = await response.Content.ReadAsStringAsync(ct);
            var snippet = body.Length > 120 ? body[..120] + "…" : body;
            return (false, $"HTTP {(int)response.StatusCode}: {snippet}");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM health probe failed for {Uri}", probeUri);
            return (false, ex.Message);
        }
    }
}
