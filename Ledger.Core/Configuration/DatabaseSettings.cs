namespace Ledger.Core.Configuration;

public class DatabaseSettings
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "Data Source=data/ledger.db";
}
