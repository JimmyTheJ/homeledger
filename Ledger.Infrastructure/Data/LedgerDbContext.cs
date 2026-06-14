using Ledger.Core.Configuration;
using Ledger.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Data;

public class LedgerDbContext : DbContext
{
    public LedgerDbContext(DbContextOptions<LedgerDbContext> options) : base(options)
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

        modelBuilder.Entity<Category>(e =>
        {
            e.HasOne(x => x.CategoryGroup)
                .WithMany(x => x.Categories)
                .HasForeignKey(x => x.CategoryGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.Name).IsUnique();
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
        });

        modelBuilder.Entity<ImportItem>(e =>
        {
            e.HasOne(x => x.SuggestedCategory).WithMany().HasForeignKey(x => x.SuggestedCategoryId);
        });
    }
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(LedgerDbContext db, CancellationToken ct = default)
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

    private static void SeedCategories(LedgerDbContext db)
    {
        var groups = new (string Name, bool IsIncome, string[] Categories)[]
        {
            ("Income", true, ["Salary", "Other Income", "Capital"]),
            ("Housing", false, ["Rent", "Mortgage", "Maintenance"]),
            ("Utilities", false, ["Gas", "Hydro", "Water", "Internet", "Phone"]),
            ("Daily Living", false, ["Groceries", "Eating Out", "Health", "Personal", "Apparel"]),
            ("Family", false, ["Kids", "Cats"]),
            ("Transport", false, ["Travel", "Auto"]),
            ("Home & Tech", false, ["Home", "Electronics", "Services"]),
            ("Lifestyle", false, ["Entertainment", "Gifts", "Charity"]),
            ("Other", false, ["Misc", "Other Expense"])
        };

        var sortOrder = 0;
        foreach (var (groupName, isIncome, categories) in groups)
        {
            var group = new CategoryGroup
            {
                Name = groupName,
                IsIncome = isIncome,
                SortOrder = sortOrder++
            };
            db.CategoryGroups.Add(group);

            var catOrder = 0;
            foreach (var catName in categories)
            {
                db.Categories.Add(new Category
                {
                    Name = catName,
                    CategoryGroup = group,
                    IsIncome = isIncome,
                    SortOrder = catOrder++
                });
            }
        }
    }
}
