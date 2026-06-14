using Ledger.Core.Configuration;
using Ledger.Core.Entities;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ledger.Infrastructure.Import;

public record CategorySuggestion(int? CategoryId, string? Notes, string Source);

public interface ITransactionCategorizer
{
    Task<CategorySuggestion> SuggestAsync(
        string description,
        decimal amount,
        IReadOnlyList<Category> categories,
        CancellationToken ct = default);
}

public class TransactionCategorizer : ITransactionCategorizer
{
    private readonly LedgerDbContext _db;
    private readonly ILlmClient _llm;
    private readonly ILogger<TransactionCategorizer> _logger;

    public TransactionCategorizer(LedgerDbContext db, ILlmClient llm, ILogger<TransactionCategorizer> logger)
    {
        _db = db;
        _llm = llm;
        _logger = logger;
    }

    public async Task<CategorySuggestion> SuggestAsync(
        string description,
        decimal amount,
        IReadOnlyList<Category> categories,
        CancellationToken ct = default)
    {
        var ruleMatch = MatchByRules(description, amount, categories);
        if (ruleMatch.CategoryId is not null)
            return ruleMatch with { Source = "rule" };

        var historyMatch = await MatchByHistoryAsync(description, ct);
        if (historyMatch.CategoryId is not null)
            return historyMatch with { Source = "history" };

        if (_llm.IsEnabled)
        {
            try
            {
                var llmMatch = await _llm.SuggestCategoryAsync(description, amount, categories, ct);
                if (llmMatch.CategoryId is not null)
                    return llmMatch with { Source = "llm" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM categorization failed for {Description}", description);
            }
        }

        var fallback = categories.FirstOrDefault(c => c.IsIncome == amount > 0);
        return new CategorySuggestion(fallback?.Id, description, "fallback");
    }

    private static CategorySuggestion MatchByRules(string description, decimal amount, IReadOnlyList<Category> categories)
    {
        var lower = description.ToLowerInvariant();
        var rules = new (string[] Keywords, string Category)[]
        {
            (["netflix", "disney", "spotify", "crave"], "Entertainment"),
            (["grocery", "costco", "loblaws", "sobeys", "metro"], "Groceries"),
            (["restaurant", "uber eats", "doordash", "tim hortons", "starbucks"], "Dining Out"),
            (["amazon", "digital ocean", "aws"], "Electronics"),
            (["hydro", "enbridge", "bell", "rogers", "telus"], "Electric"),
            (["payroll", "salary", "deposit"], "Salary"),
        };

        foreach (var (keywords, categoryName) in rules)
        {
            if (keywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
            {
                var cat = categories.FirstOrDefault(c =>
                    c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
                if (cat is not null)
                    return new CategorySuggestion(cat.Id, description, "rule");
            }
        }

        return new CategorySuggestion(null, description, "rule");
    }

    private async Task<CategorySuggestion> MatchByHistoryAsync(string description, CancellationToken ct)
    {
        var needle = description.Trim().ToLowerInvariant();
        var match = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Notes.ToLower().Contains(needle) || needle.Contains(t.Notes.ToLower()))
            .OrderByDescending(t => t.Date)
            .Select(t => new { t.CategoryId, t.Notes })
            .FirstOrDefaultAsync(ct);

        return match is null
            ? new CategorySuggestion(null, description, "history")
            : new CategorySuggestion(match.CategoryId, match.Notes, "history");
    }
}

public interface ILlmClient
{
    bool IsEnabled { get; }
    Task<CategorySuggestion> SuggestCategoryAsync(
        string description,
        decimal amount,
        IReadOnlyList<Category> categories,
        CancellationToken ct = default);
}

public class LlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly LlmSettings _settings;

    public LlmClient(HttpClient http, IOptions<LlmSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    public bool IsEnabled => _settings.Enabled && _settings.UseForCategorization;

    public async Task<CategorySuggestion> SuggestCategoryAsync(
        string description,
        decimal amount,
        IReadOnlyList<Category> categories,
        CancellationToken ct = default)
    {
        var categoryList = string.Join(", ", categories.Select(c => c.Name));
        var prompt = $$"""
            Categorize this bank transaction. Reply with JSON only: {"category":"name","notes":"brief note"}
            Amount: {{amount:F2}}
            Description: {{description}}
            Valid categories: {{categoryList}}
            """;

        var request = new
        {
            model = _settings.DefaultModel,
            messages = new[]
            {
                new { role = "system", content = "You are a personal finance assistant. Respond with valid JSON only." },
                new { role = "user", content = prompt }
            },
            temperature = 0.1
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
        var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        var parsed = JsonSerializer.Deserialize<LlmCategoryResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var cat = categories.FirstOrDefault(c =>
            c.Name.Equals(parsed?.Category, StringComparison.OrdinalIgnoreCase));

        return new CategorySuggestion(cat?.Id, parsed?.Notes ?? description, "llm");
    }

    private sealed class LlmCategoryResponse
    {
        public string? Category { get; set; }
        public string? Notes { get; set; }
    }
}

public class NullLlmClient : ILlmClient
{
    public bool IsEnabled => false;

    public Task<CategorySuggestion> SuggestCategoryAsync(
        string description,
        decimal amount,
        IReadOnlyList<Category> categories,
        CancellationToken ct = default) =>
        Task.FromResult(new CategorySuggestion(null, description, "disabled"));
}
