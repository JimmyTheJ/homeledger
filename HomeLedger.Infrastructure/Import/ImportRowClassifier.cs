using System.Net.Http.Json;
using System.Text.Json;
using HomeLedger.Core.Configuration;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Import;

public record ImportRowClassification(string Action, string? SkipKind, string? Reason);

public interface IImportRowClassifier
{
    bool IsEnabled { get; }
    Task<IReadOnlyDictionary<int, ImportRowClassification>> ClassifyBatchAsync(
        IReadOnlyList<ImportClassificationRequest> rows,
        CancellationToken ct = default);
}

public record ImportClassificationRequest(int Index, string Description, decimal Amount);

public class ImportRowClassifier : IImportRowClassifier
{
    private readonly HttpClient _http;
    private readonly LlmSettings _settings;

    public ImportRowClassifier(HttpClient http, IOptions<LlmSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    public bool IsEnabled =>
        _settings.Enabled
        && _settings.UseForImportClassification
        && (!string.IsNullOrWhiteSpace(_settings.ApiKey)
            || (_settings.ResolvedProvider == LlmProvider.OpenAiCompatible
                && _settings.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)));

    public async Task<IReadOnlyDictionary<int, ImportRowClassification>> ClassifyBatchAsync(
        IReadOnlyList<ImportClassificationRequest> rows,
        CancellationToken ct = default)
    {
        if (!IsEnabled || rows.Count == 0)
            return new Dictionary<int, ImportRowClassification>();

        var lines = string.Join("\n", rows.Select(r => $"{r.Index}|{r.Amount:F2}|{r.Description}"));
        var prompt = $$"""
            Classify bank CSV rows for a personal budget app (not full double-entry accounting).
            Skip rows that are internal transfers, credit card payments, investment moves, or reimbursements — not real income/expense.
            Import rows that are payroll, merchant purchases, utilities, fees, e-transfers to payees, etc.

            Reply JSON only:
            {"rows":[{"index":0,"action":"import"|"skip","kind":"credit_card_payment"|"internal_transfer"|"investment_transfer"|"reimbursement"|"expense"|"income","reason":"brief"}]}

            Rows (index|amount|description):
            {{lines}}
            """;

        var request = new
        {
            model = _settings.DefaultModel,
            messages = new[]
            {
                new { role = "system", content = "You classify bank transactions for import. JSON only." },
                new { role = "user", content = prompt }
            },
            temperature = 0
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request)
        };

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await _http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        var parsed = LlmJson.Deserialize<ClassificationResponse>(content);

        var result = new Dictionary<int, ImportRowClassification>();
        if (parsed?.Rows is null)
            return result;

        foreach (var row in parsed.Rows)
        {
            if (row.Index is null)
                continue;

            var action = row.Action?.Equals("skip", StringComparison.OrdinalIgnoreCase) == true ? "skip" : "import";
            var skipKind = MapKind(row.Kind);
            result[row.Index.Value] = new ImportRowClassification(action, skipKind, row.Reason);
        }

        return result;
    }

    private static string? MapKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "credit_card_payment" => ImportSkipReasons.CreditCardPayment,
        "internal_transfer" => ImportSkipReasons.InternalTransfer,
        "investment_transfer" => ImportSkipReasons.InvestmentTransfer,
        "reimbursement" => ImportSkipReasons.Reimbursement,
        _ => ImportSkipReasons.LlmSuggestedSkip
    };

    private sealed class ClassificationResponse
    {
        public List<ClassificationRow>? Rows { get; set; }
    }

    private sealed class ClassificationRow
    {
        public int? Index { get; set; }
        public string? Action { get; set; }
        public string? Kind { get; set; }
        public string? Reason { get; set; }
    }
}

public class NullImportRowClassifier : IImportRowClassifier
{
    public bool IsEnabled => false;

    public Task<IReadOnlyDictionary<int, ImportRowClassification>> ClassifyBatchAsync(
        IReadOnlyList<ImportClassificationRequest> rows,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<int, ImportRowClassification>>(new Dictionary<int, ImportRowClassification>());
}
