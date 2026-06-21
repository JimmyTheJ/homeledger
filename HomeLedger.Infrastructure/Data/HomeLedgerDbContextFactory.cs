using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HomeLedger.Infrastructure;

public class HomeLedgerDbContextFactory : IDesignTimeDbContextFactory<HomeLedgerDbContext>
{
    public HomeLedgerDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../HomeLedger.Web"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration.GetSection(DatabaseSettings.SectionName)
            .Get<DatabaseSettings>() ?? new DatabaseSettings();

        var optionsBuilder = new DbContextOptionsBuilder<HomeLedgerDbContext>();
        HomeLedgerDbContextOptions.Configure(
            optionsBuilder,
            settings.ResolvedProvider,
            settings.ResolvedConnectionString);

        return new HomeLedgerDbContext(optionsBuilder.Options);
    }
}
