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
            .Build();

        var connectionString = configuration.GetSection("Database")["ConnectionString"]
            ?? "Data Source=data/homeledger.db";

        var optionsBuilder = new DbContextOptionsBuilder<HomeLedgerDbContext>();
        optionsBuilder.UseSqlite(connectionString);
        return new HomeLedgerDbContext(optionsBuilder.Options);
    }
}
