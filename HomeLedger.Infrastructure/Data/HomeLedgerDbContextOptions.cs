using System.Data.Common;
using HomeLedger.Core.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HomeLedger.Infrastructure.Data;

public static class HomeLedgerDbContextOptions
{
    public const string SqliteMigrationsAssembly = "HomeLedger.Migrations.Sqlite";
    public const string PostgresMigrationsAssembly = "HomeLedger.Migrations.PostgreSql";

    public static void Configure(
        DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case DatabaseProvider.Postgres:
                // Treat DateTime values as `timestamp without time zone` and skip the
                // strict UTC Kind enforcement Npgsql applies by default. This keeps
                // CSV-imported dates (which may have an unspecified Kind) working.
                AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
                break;

            default:
                var sqlite = new SqliteConnectionStringBuilder(connectionString)
                {
                    Cache = SqliteCacheMode.Shared,
                    DefaultTimeout = 5
                };
                options.UseSqlite(sqlite.ToString(), sqliteOptions =>
                    sqliteOptions.MigrationsAssembly(SqliteMigrationsAssembly));
                options.AddInterceptors(SqlitePragmaConnectionInterceptor.Instance);
                break;
        }
    }
}

internal sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public static SqlitePragmaConnectionInterceptor Instance { get; } = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Apply(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        if (connection is not SqliteConnection)
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
    }
}
