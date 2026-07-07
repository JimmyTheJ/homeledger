using HomeLedger.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HomeLedger.Infrastructure.Import;

public class ReceiptInboxPathResolver
{
    private readonly ReceiptInboxSettings _settings;
    private readonly string _contentRoot;
    private string? _resolvedWatchPath;

    public ReceiptInboxPathResolver(IOptions<ReceiptInboxSettings> settings, IHostEnvironment environment)
    {
        _settings = settings.Value;
        _contentRoot = environment.ContentRootPath;
    }

    public string ResolveWatchPath()
    {
        if (_resolvedWatchPath is not null)
            return _resolvedWatchPath;

        _resolvedWatchPath = Path.IsPathRooted(_settings.WatchPath)
            ? Path.GetFullPath(_settings.WatchPath)
            : Path.GetFullPath(Path.Combine(_contentRoot, _settings.WatchPath));

        return _resolvedWatchPath;
    }

    public bool IsWithinWatchRoot(string fullPath)
    {
        var watchRoot = ResolveWatchPath();
        var normalized = Path.GetFullPath(fullPath);
        return normalized.Equals(watchRoot, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(watchRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
