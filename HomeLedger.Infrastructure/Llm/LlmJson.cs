using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HomeLedger.Infrastructure.Llm;

internal static partial class LlmJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new FlexibleDecimalConverter(), new FlexibleBoolConverter() }
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T? Deserialize<T>(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return default;

        var json = ExtractJson(content);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            var normalized = NormalizePropertyNames(json);
            return JsonSerializer.Deserialize<T>(normalized, SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    internal static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new StringBuilder(name.Length);
        var upperNext = true;
        foreach (var c in name)
        {
            if (c is '_' or '-')
            {
                upperNext = true;
                continue;
            }

            builder.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }

        return builder.ToString();
    }

    private static string NormalizePropertyNames(string json)
    {
        using var doc = JsonDocument.Parse(json, DocumentOptions);
        var node = Normalize(doc.RootElement);
        return node?.ToJsonString() ?? json;
    }

    private static JsonNode? Normalize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => NormalizeObject(element),
        JsonValueKind.Array => NormalizeArray(element),
        JsonValueKind.String => JsonValue.Create(element.GetString()),
        JsonValueKind.Number => JsonNode.Parse(element.GetRawText()),
        JsonValueKind.True => JsonValue.Create(true),
        JsonValueKind.False => JsonValue.Create(false),
        _ => null
    };

    private static JsonObject NormalizeObject(JsonElement element)
    {
        var obj = new JsonObject();
        foreach (var property in element.EnumerateObject())
            obj[ToPascalCase(property.Name)] = Normalize(property.Value);
        return obj;
    }

    private static JsonArray NormalizeArray(JsonElement element)
    {
        var array = new JsonArray();
        foreach (var item in element.EnumerateArray())
            array.Add(Normalize(item));
        return array;
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

    private sealed class FlexibleDecimalConverter : JsonConverter<decimal?>
    {
        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.Number:
                    return reader.GetDecimal();
                case JsonTokenType.String:
                    return TryParseDecimal(reader.GetString());
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteNumberValue(value.Value);
        }

        private static decimal? TryParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var cleaned = value.Trim()
                .Replace("$", "", StringComparison.Ordinal)
                .Replace("€", "", StringComparison.Ordinal)
                .Replace("£", "", StringComparison.Ordinal)
                .Replace(",", "", StringComparison.Ordinal);
            if (decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }
    }

    private sealed class FlexibleBoolConverter : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var number) ? number != 0 : null;
                case JsonTokenType.String:
                    return TryParseBool(reader.GetString());
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteBooleanValue(value.Value);
        }

        private static bool? TryParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim().ToLowerInvariant() switch
            {
                "true" or "yes" or "1" => true,
                "false" or "no" or "0" => false,
                _ => null
            };
        }
    }
}
