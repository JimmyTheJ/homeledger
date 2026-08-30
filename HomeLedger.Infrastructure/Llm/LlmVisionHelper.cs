using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HomeLedger.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HomeLedger.Infrastructure.Llm;

internal static class LlmVisionHelper
{
    internal const int ErrorBodyLogLimit = 1500;

    public static async Task<string> CompleteAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        ILogger logger,
        CancellationToken ct)
    {
        return settings.ResolvedProvider switch
        {
            LlmProvider.Anthropic => await CallAnthropicAsync(http, settings, prompt, pages, logger, ct),
            LlmProvider.Gemini => await CallGeminiAsync(http, settings, prompt, pages, logger, ct),
            _ when settings.LooksLikeOllama() => await CallOllamaNativeAsync(http, settings, prompt, pages, logger, ct),
            _ => await CallOpenAiCompatibleAsync(http, settings, prompt, pages, logger, ct)
        };
    }

    private static async Task<string> CallOllamaNativeAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        ILogger logger,
        CancellationToken ct)
    {
        var options = new Dictionary<string, object>
        {
            ["temperature"] = 0,
            ["num_predict"] = settings.ResolvedVisionMaxTokens
        };
        if (settings.ResolvedNumCtx > 0)
            options["num_ctx"] = settings.ResolvedNumCtx;

        var request = new
        {
            model = settings.ResolvedVisionModel,
            stream = false,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt,
                    images = pages.Select(page => Convert.ToBase64String(page.PngBytes)).ToArray()
                }
            },
            options
        };

        logger.LogInformation(
            "Using Ollama native /api/chat for {Model} (num_ctx {NumCtx}, num_predict {MaxTokens})",
            settings.ResolvedVisionModel,
            settings.ResolvedNumCtx > 0 ? settings.ResolvedNumCtx : 0,
            settings.ResolvedVisionMaxTokens);

        using var msg = new HttpRequestMessage(HttpMethod.Post, ResolveOllamaNativeChatUri(http.BaseAddress))
        {
            Content = JsonContent.Create(request)
        };
        ApplyOpenAiAuth(msg, settings);

        var json = await SendAndReadJsonAsync(http, msg, settings, logger, ct);
        if (!json.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            return "{}";
        }

        return content.GetString() ?? "{}";
    }

    private static async Task<string> CallOpenAiCompatibleAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        ILogger logger,
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
            max_tokens = settings.ResolvedVisionMaxTokens
        };

        object payload = settings.ResolvedNumCtx > 0
            ? new
            {
                request.model,
                request.messages,
                request.temperature,
                request.max_tokens,
                num_ctx = settings.ResolvedNumCtx,
                options = new { num_ctx = settings.ResolvedNumCtx }
            }
            : request;

        using var msg = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        ApplyOpenAiAuth(msg, settings);

        var json = await SendAndReadJsonAsync(http, msg, settings, logger, ct);
        return ReadOpenAiMessageContent(json);
    }

    private static async Task<string> CallAnthropicAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        ILogger logger,
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

        var json = await SendAndReadJsonAsync(http, msg, settings, logger, ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";
    }

    private static async Task<string> CallGeminiAsync(
        HttpClient http,
        LlmSettings settings,
        string prompt,
        IReadOnlyList<StatementPageImage> pages,
        ILogger logger,
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

        var json = await SendAndReadJsonAsync(http, msg, settings, logger, ct);
        return json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "{}";
    }

    private static async Task<JsonElement> SendAndReadJsonAsync(
        HttpClient http,
        HttpRequestMessage msg,
        LlmSettings settings,
        ILogger logger,
        CancellationToken ct)
    {
        using var response = await http.SendAsync(msg, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = PreviewResponseBody(body);
            logger.LogWarning(
                "Vision model {Model} returned HTTP {StatusCode} {ReasonPhrase}. Response body: {Body}",
                settings.ResolvedVisionModel,
                (int)response.StatusCode,
                response.ReasonPhrase,
                preview);
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). {preview}",
                inner: null,
                response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(body))
            return JsonSerializer.Deserialize<JsonElement>("{}");

        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    internal static string PreviewResponseBody(string? body, int maxChars = ErrorBodyLogLimit)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "<empty>";

        var trimmed = string.Join(" ", body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "…";
    }

    internal static bool IsModelRunnerAssert(Exception ex)
    {
        var text = ex.Message;
        if (ex.InnerException is not null)
            text += " " + ex.InnerException.Message;

        return text.Contains("GGML_ASSERT", StringComparison.OrdinalIgnoreCase)
            || text.Contains("signal arrived during cgo", StringComparison.OrdinalIgnoreCase)
            || text.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cudaMalloc", StringComparison.OrdinalIgnoreCase);
    }

    internal static Uri ResolveOllamaNativeChatUri(Uri? baseAddress)
    {
        if (baseAddress is null)
            return new Uri("/api/chat", UriKind.Relative);

        return new Uri($"{baseAddress.GetLeftPart(UriPartial.Authority)}/api/chat");
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
