namespace HomeLedger.Core.Configuration;

public enum DatabaseProvider
{
    Sqlite,
    Postgres
}

public static class DatabaseProviderDefaults
{
    public static string ConnectionString(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Postgres => "Host=localhost;Port=5432;Database=homeledger;Username=postgres;Password=postgres",
        _ => "Data Source=data/homeledger.db"
    };

    public static DatabaseProvider Parse(string? value) =>
        Enum.TryParse<DatabaseProvider>(value, true, out var provider)
            ? provider
            : DatabaseProvider.Sqlite;
}
