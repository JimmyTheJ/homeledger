using System.Text.Json;
using System.Text.RegularExpressions;

namespace HomeLedger.Infrastructure.Llm;

internal static partial class LlmJson
{
    public static T? Deserialize<T>(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return default;

        var json = ExtractJson(content);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();
        var fenced = FenceRegex().Match(trimmed);
        if (fenced.Success)
            return fenced.Groups[1].Value.Trim();

        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');
        var start = objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart)
            ? objectStart
            : arrayStart;

        if (start < 0)
            return trimmed;

        var open = trimmed[start];
        var close = open == '{' ? '}' : ']';
        var depth = 0;
        for (var i = start; i < trimmed.Length; i++)
        {
            if (trimmed[i] == open)
                depth++;
            else if (trimmed[i] == close)
            {
                depth--;
                if (depth == 0)
                    return trimmed[start..(i + 1)];
            }
        }

        return trimmed[start..];
    }

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex FenceRegex();
}
