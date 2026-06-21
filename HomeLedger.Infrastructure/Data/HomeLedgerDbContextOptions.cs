using HomeLedger.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Infrastructure.Data;

public static class HomeLedgerDbContextOptions
{
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
                options.UseSqlite(connectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(HomeLedgerDbContext).Assembly.GetName().Name));
                break;
        }
    }
}
