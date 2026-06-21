namespace HomeLedger.Core.Configuration;

public class DatabaseSettings
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = nameof(DatabaseProvider.Sqlite);

    public string? ConnectionString { get; set; }

    public DatabaseProvider ResolvedProvider => DatabaseProviderDefaults.Parse(Provider);

    public string ResolvedConnectionString =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? DatabaseProviderDefaults.ConnectionString(ResolvedProvider)
            : ConnectionString;
}
