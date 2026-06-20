using System.Net;

namespace HomeLedger.Core.Configuration;

public static class LlmSettingsExtensions
{
    public static bool HasEffectiveApiAccess(this LlmSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ApiKey)
        || (settings.ResolvedProvider == LlmProvider.OpenAiCompatible
            && IsLikelyLocalOrPrivateEndpoint(settings.BaseUrl));

    public static bool IsCategorizationEffective(this LlmSettings settings) =>
        settings.Enabled && settings.UseForCategorization;

    public static bool IsImportClassificationEffective(this LlmSettings settings) =>
        settings.Enabled && settings.UseForImportClassification && settings.HasEffectiveApiAccess();

    public static bool IsStatementImportEffective(this LlmSettings settings) =>
        settings.Enabled && settings.UseForStatementImport && settings.HasEffectiveApiAccess();

    public static string? DescribeCategorizationBlocker(this LlmSettings settings)
    {
        if (!settings.Enabled)
            return "LLM is disabled in configuration.";
        if (!settings.UseForCategorization)
            return "UseForCategorization is off.";
        return null;
    }

    public static string? DescribeImportClassificationBlocker(this LlmSettings settings)
    {
        if (!settings.Enabled)
            return "LLM is disabled in configuration.";
        if (!settings.UseForImportClassification)
            return "UseForImportClassification is off.";
        if (!settings.HasEffectiveApiAccess())
            return "No API key and BaseUrl does not look like a local/private Ollama endpoint.";
        return null;
    }

    public static string? DescribeStatementImportBlocker(this LlmSettings settings)
    {
        if (!settings.Enabled)
            return "LLM is disabled in configuration.";
        if (!settings.UseForStatementImport)
            return "UseForStatementImport is off.";
        if (!settings.HasEffectiveApiAccess())
            return "No API key and BaseUrl does not look like a local/private Ollama endpoint.";
        return null;
    }

    public static bool IsLikelyLocalOrPrivateEndpoint(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
            return IPAddress.IsLoopback(ip) || IsPrivateNetwork(ip);

        // Docker service names and single-label LAN hosts (e.g. aiweb_ollama).
        return !host.Contains('.');
    }

    private static bool IsPrivateNetwork(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            _ => false
        };
    }

    public static Uri? ResolveHealthProbeUri(this LlmSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            return null;

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
            return null;

        if (settings.ResolvedProvider == LlmProvider.OpenAiCompatible)
        {
            var root = settings.BaseUrl.TrimEnd('/');
            if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                root = root[..^3];

            return new Uri($"{root}/api/tags");
        }

        return baseUri;
    }
}
