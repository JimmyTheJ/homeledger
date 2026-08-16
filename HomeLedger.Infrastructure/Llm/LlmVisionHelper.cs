using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeLedger.Core.Configuration;

namespace HomeLedger.Infrastructure.Llm;

internal static class LlmVisionHelper
{
    public static async Task<string> CompleteAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct)
    {
        return settings.ResolvedProvider switch
        {
            LlmProvider.Anthropic => await CallAnthropicAsync(http, settings, prompt, pages, ct),
            LlmProvider.Gemini => await CallGeminiAsync(http, settings, prompt, pages, ct),
            _ => await CallOpenAiCompatibleAsync(http, settings, prompt, pages, ct)
        };
    }

    private static async Task<string> CallOpenAiCompatibleAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct)
    {
        var content = new List<object>
        {
            new { type = "text", text = prompt }
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
            model = settings.ResolvedVisionModel,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = content.ToArray()
                }
            },
            temperature = 0,
            max_tokens = 4096
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request)
        };
        ApplyOpenAiAuth(msg, settings);

        using var response = await http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ReadOpenAiMessageContent(json);
    }

    private static async Task<string> CallAnthropicAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct)
    {
        var content = new List<object>
        {
            new { type = "text", text = prompt }
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
            model = settings.ResolvedVisionModel,
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
        msg.Headers.Add("x-api-key", settings.ApiKey);
        msg.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";
    }

    private static async Task<string> CallGeminiAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        CancellationToken ct)
    {
        var parts = new List<object>
        {
            new { text = prompt }
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

        var model = Uri.EscapeDataString(settings.ResolvedVisionModel);
        var url = $"/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(settings.ApiKey ?? "")}";
        using var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        using var response = await http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "{}";
    }

    private static void ApplyOpenAiAuth(HttpRequestMessage msg, LlmSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
    }

    internal static string ReadOpenAiMessageContent(JsonElement json)
    {
        if (!json.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return "{}";
        }

        if (!choices[0].TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            return "{}";
        }

        return ReadContent(content);
    }

    private static string ReadContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "{}";

        if (content.ValueKind == JsonValueKind.Array)
        {
            var text = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    text.Append(part.GetString());
                    continue;
                }

                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var partText))
                    text.Append(partText.GetString());
            }

            return text.Length == 0 ? "{}" : text.ToString();
        }

        return "{}";
    }
}
