using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ledger.Core.Configuration;
using Ledger.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Llm;

public class LlmStatementExtractor : ILlmStatementExtractor
{
    private const string ExtractionPrompt = """
        Extract every individual transaction line from these bank or credit card statement page images.

        Return JSON only in this exact shape:
        {"transactions":[{"date":"yyyy/MM/dd","amount":-12.34,"description":"merchant or memo","external_id":null}]}

        Rules:
        - Include purchases, payments, deposits, fees, refunds, and transfers shown as line items.
        - amount must be negative for debits/charges/payments out and positive for credits/deposits/payments in.
        - Use yyyy/MM/dd for dates. Infer the statement year when only month/day is shown.
        - description should be the merchant, payee, or memo text for the line.
        - external_id should be a reference/check/confirmation number when visible, otherwise null.
        - Skip headers, footers, section titles, running balances, opening/closing balances, and summary totals.
        - Do not invent transactions that are not visible in the images.
        - Combine multi-page duplicates only once.
        """;

    private readonly HttpClient _http;
    private readonly LlmSettings _settings;
    private readonly ILogger<LlmStatementExtractor> _logger;

    public LlmStatementExtractor(HttpClient http, IOptions<LlmSettings> settings, ILogger<LlmStatementExtractor> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            if (!_settings.Enabled || !_settings.UseForStatementImport)
                return false;

            if (_settings.ResolvedProvider == LlmProvider.OpenAiCompatible
                && _settings.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(_settings.ApiKey);
        }
    }

    public async Task<IReadOnlyList<ExtractedStatementLine>> ExtractAsync(
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct = default)
    {
        if (pages.Count == 0)
            return [];

        var responseText = _settings.ResolvedProvider switch
        {
            LlmProvider.Anthropic => await CallAnthropicAsync(pages, ct),
            LlmProvider.Gemini => await CallGeminiAsync(pages, ct),
            _ => await CallOpenAiCompatibleAsync(pages, ct)
        };

        var parsed = LlmJson.Deserialize<StatementExtractionResponse>(responseText);
        if (parsed?.Transactions is null || parsed.Transactions.Count == 0)
        {
            _logger.LogWarning("LLM returned no transactions. Raw response length: {Length}", responseText?.Length ?? 0);
            return [];
        }

        var lines = new List<ExtractedStatementLine>();
        foreach (var row in parsed.Transactions)
        {
            if (row.Amount is null || string.IsNullOrWhiteSpace(row.Description))
                continue;

            if (!TryParseDate(row.Date, out var date))
                continue;

            lines.Add(new ExtractedStatementLine(
                date,
                row.Amount.Value,
                row.Description.Trim(),
                string.IsNullOrWhiteSpace(row.ExternalId) ? null : row.ExternalId.Trim()));
        }

        return Deduplicate(lines);
    }

    private async Task<string> CallOpenAiCompatibleAsync(IReadOnlyList<StatementPageImage> pages, CancellationToken ct)
    {
        var content = new List<object>
        {
            new { type = "text", text = ExtractionPrompt }
        };

        foreach (var page in pages)
        {
            content.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:{page.MimeType};base64,{Convert.ToBase64String(page.PngBytes)}"
                }
            });
        }

        var request = new
        {
            model = _settings.ResolvedVisionModel,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = content.ToArray()
                }
            },
            temperature = 0
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request)
        };
        ApplyOpenAiAuth(msg);

        using var response = await _http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
    }

    private async Task<string> CallAnthropicAsync(IReadOnlyList<StatementPageImage> pages, CancellationToken ct)
    {
        var content = new List<object>
        {
            new { type = "text", text = ExtractionPrompt }
        };

        foreach (var page in pages)
        {
            content.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = page.MimeType,
                    data = Convert.ToBase64String(page.PngBytes)
                }
            });
        }

        var request = new
        {
            model = _settings.ResolvedVisionModel,
            max_tokens = 8192,
            temperature = 0,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = content.ToArray()
                }
            }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        msg.Headers.Add("x-api-key", _settings.ApiKey);
        msg.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await _http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";
    }

    private async Task<string> CallGeminiAsync(IReadOnlyList<StatementPageImage> pages, CancellationToken ct)
    {
        var parts = new List<object>
        {
            new { text = ExtractionPrompt }
        };

        foreach (var page in pages)
        {
            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = page.MimeType,
                    data = Convert.ToBase64String(page.PngBytes)
                }
            });
        }

        var request = new
        {
            contents = new[]
            {
                new { parts = parts.ToArray() }
            },
            generationConfig = new
            {
                temperature = 0
            }
        };

        var model = Uri.EscapeDataString(_settings.ResolvedVisionModel);
        var url = $"/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(_settings.ApiKey ?? "")}";
        using var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "{}";
    }

    private void ApplyOpenAiAuth(HttpRequestMessage msg)
    {
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (LedgerFormats.TryParseDate(value, out date))
            return true;

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static IReadOnlyList<ExtractedStatementLine> Deduplicate(IEnumerable<ExtractedStatementLine> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ExtractedStatementLine>();

        foreach (var line in lines)
        {
            var key = !string.IsNullOrWhiteSpace(line.ExternalId)
                ? $"id:{line.ExternalId}"
                : $"row:{line.Date:yyyyMMdd}:{line.Amount}:{line.Description.Trim().ToLowerInvariant()}";

            if (!seen.Add(key))
                continue;

            result.Add(line);
        }

        return result;
    }

    private sealed class StatementExtractionResponse
    {
        public List<StatementExtractionLine> Transactions { get; set; } = [];
    }

    private sealed class StatementExtractionLine
    {
        public string? Date { get; set; }
        public decimal? Amount { get; set; }
        public string? Description { get; set; }
        public string? ExternalId { get; set; }
    }
}
