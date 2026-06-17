namespace HomeLedger.Core.Configuration;

public enum LlmProvider
{
    OpenAiCompatible,
    Anthropic,
    Gemini
}

public static class LlmProviderDefaults
{
    public static string BaseUrl(LlmProvider provider) => provider switch
    {
        LlmProvider.Anthropic => "https://api.anthropic.com",
        LlmProvider.Gemini => "https://generativelanguage.googleapis.com",
        _ => "https://api.openai.com/v1"
    };

    public static string VisionModel(LlmProvider provider) => provider switch
    {
        LlmProvider.Anthropic => "claude-sonnet-4-20250514",
        LlmProvider.Gemini => "gemini-2.0-flash",
        _ => "gpt-4o"
    };

    public static string TextModel(LlmProvider provider) => provider switch
    {
        LlmProvider.Anthropic => "claude-sonnet-4-20250514",
        LlmProvider.Gemini => "gemini-2.0-flash",
        _ => "gpt-4o-mini"
    };

    public static LlmProvider Parse(string? value) =>
        Enum.TryParse<LlmProvider>(value, true, out var provider)
            ? provider
            : LlmProvider.OpenAiCompatible;
}
