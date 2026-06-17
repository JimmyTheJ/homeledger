using HomeLedger.Core.Configuration;
using HomeLedger.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Infrastructure.Data;

public class HomeLedgerDbContext : DbContext
{
    public HomeLedgerDbContext(DbContextOptions<HomeLedgerDbContext> options) : base(options)
    {
    }

    public DbSet<LedgerEntity> Entities => Set<LedgerEntity>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<CategoryGroup> CategoryGroups => Set<CategoryGroup>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<BudgetLimit> BudgetLimits => Set<BudgetLimit>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportItem> ImportItems => Set<ImportItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LedgerEntity>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Account>(e =>
        {
            e.HasOne(x => x.LedgerEntity)
                .WithMany(x => x.Accounts)
                .HasForeignKey(x => x.LedgerEntityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CategoryGroup>(e =>
        {
            e.HasOne(x => x.LedgerEntity).WithMany().HasForeignKey(x => x.LedgerEntityId);
            e.HasIndex(x => new { x.Name, x.LedgerEntityId }).IsUnique();
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasOne(x => x.CategoryGroup)
                .WithMany(x => x.Categories)
                .HasForeignKey(x => x.CategoryGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.LedgerEntity).WithMany().HasForeignKey(x => x.LedgerEntityId);
            e.HasIndex(x => new { x.Name, x.LedgerEntityId }).IsUnique();
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasOne(x => x.Category).WithMany(x => x.Transactions).HasForeignKey(x => x.CategoryId);
            e.HasOne(x => x.LedgerEntity).WithMany(x => x.Transactions).HasForeignKey(x => x.LedgerEntityId);
            e.HasOne(x => x.Account).WithMany(x => x.Transactions).HasForeignKey(x => x.AccountId);

            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.ExternalId, x.AccountId }).IsUnique()
                .HasFilter("[ExternalId] IS NOT NULL");
        });

        modelBuilder.Entity<BudgetLimit>(e =>
        {
            e.HasOne(x => x.Category).WithMany(x => x.BudgetLimits).HasForeignKey(x => x.CategoryId);
            e.HasOne(x => x.LedgerEntity).WithMany().HasForeignKey(x => x.LedgerEntityId);
        });

        modelBuilder.Entity<ImportBatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Items).WithOne(x => x.ImportBatch).HasForeignKey(x => x.ImportBatchId);
            e.HasIndex(x => new { x.FileSha256, x.FileSizeBytes, x.AccountId });
        });

        modelBuilder.Entity<ImportItem>(e =>
        {
            e.HasOne(x => x.SuggestedCategory).WithMany().HasForeignKey(x => x.SuggestedCategoryId);
        });
    }
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(HomeLedgerDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (await db.Entities.AnyAsync(ct))
            return;

        var household = new LedgerEntity { Name = "Household" };
        db.Entities.Add(household);

        db.Accounts.Add(new Account
        {
            Name = "Primary Chequing",
            Institution = "Bank",
            LedgerEntity = household
        });

        SeedCategories(db);
        await db.SaveChangesAsync(ct);
    }

    public static async Task UpgradeLegacyBaselineAsync(HomeLedgerDbContext db, CancellationToken ct = default)
    {
        if (await db.CategoryGroups.AnyAsync(g => g.LedgerEntityId == null && g.Name == "Employment Income", ct))
            return;

        var legacyMarkers = new[] { "Daily Living", "Family", "Transport", "Home & Tech", "Cats" };
        var hasLegacy = await db.Categories.AnyAsync(
            c => c.LedgerEntityId == null && legacyMarkers.Contains(c.Name), ct);
        if (!hasLegacy)
            return;

        var globalGroups = await db.CategoryGroups
            .Where(g => g.LedgerEntityId == null)
            .Include(g => g.Categories)
            .ToListAsync(ct);

        foreach (var group in globalGroups)
        {
            group.IsActive = false;
            foreach (var category in group.Categories)
                category.IsActive = false;
        }

        BaselineCategorySeed.Seed(db);
        await db.SaveChangesAsync(ct);
    }

    private static void SeedCategories(HomeLedgerDbContext db) => BaselineCategorySeed.Seed(db);
}
