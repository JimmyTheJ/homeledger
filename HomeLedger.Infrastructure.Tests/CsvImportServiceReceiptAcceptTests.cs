using HomeLedger.Core.Configuration;
using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Import;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class CsvImportServiceReceiptAcceptTests
{
    [Fact]
    public async Task AcceptReceiptBatch_allows_shared_ocr_receipt_number_on_line_items()
    {
        await using var harness = await ReceiptAcceptHarness.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        var batch = await harness.CreatePendingReceiptBatchAsync(
            "Walmart",
            "R-1001",
            (date, -2.47m, "Bananas"),
            (date, -4.99m, "Milk"));

        var result = await harness.Import.AcceptReceiptBatchAsync(
            harness.AcceptRequest(batch),
            CancellationToken.None);

        Assert.Equal(ImportAcceptStatus.Accepted, result.Status);
        Assert.NotNull(result.ReceiptTransaction);
        Assert.Equal("R-1001", result.ReceiptTransaction.ExternalId);
        Assert.Equal(-7.46m, result.ReceiptTransaction.Amount);

        var saved = await harness.Db.Transactions.AsNoTracking()
            .Where(t => t.ImportBatchId == batch.Id)
            .OrderBy(t => t.Id)
            .ToListAsync();
        Assert.Equal(3, saved.Count);
        Assert.Equal(TransactionKind.Receipt, saved[0].Kind);
        Assert.Equal("R-1001", saved[0].ExternalId);
        Assert.All(saved.Where(t => t.Kind == TransactionKind.ReceiptLine), line =>
            Assert.Null(line.ExternalId));
    }

    [Fact]
    public async Task AcceptReceiptBatch_omits_receipt_number_when_account_already_has_it()
    {
        await using var harness = await ReceiptAcceptHarness.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        harness.Db.Transactions.Add(new Transaction
        {
            Date = date.AddDays(-3),
            Amount = -40.00m,
            Kind = TransactionKind.Standard,
            LedgerEntityId = harness.EntityId,
            AccountId = harness.AccountId,
            Notes = "Bank POS",
            ExternalId = "R-1001"
        });
        await harness.Db.SaveChangesAsync();

        var batch = await harness.CreatePendingReceiptBatchAsync(
            "Walmart",
            "R-1001",
            (date, -2.47m, "Bananas"),
            (date, -4.99m, "Milk"));

        var result = await harness.Import.AcceptReceiptBatchAsync(
            harness.AcceptRequest(batch),
            CancellationToken.None);

        Assert.Equal(ImportAcceptStatus.Accepted, result.Status);
        Assert.Null(result.ReceiptTransaction?.ExternalId);
        Assert.Equal(2, await harness.Db.Transactions.CountAsync(t => t.Kind == TransactionKind.ReceiptLine));
    }

    [Fact]
    public async Task AcceptReceiptBatch_resumes_partial_save_onto_existing_parent()
    {
        await using var harness = await ReceiptAcceptHarness.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        var batch = await harness.CreatePendingReceiptBatchAsync(
            "Walmart",
            "R-1001",
            (date, -2.47m, "Bananas"),
            (date, -4.99m, "Milk"));

        var first = batch.Items.OrderBy(i => i.Id).First();
        var parent = new Transaction
        {
            Date = date,
            Amount = -2.47m,
            Kind = TransactionKind.Receipt,
            LedgerEntityId = harness.EntityId,
            AccountId = harness.AccountId,
            Notes = "Walmart",
            Merchant = "Walmart",
            ImportBatchId = batch.Id
        };
        harness.Db.Transactions.Add(parent);
        await harness.Db.SaveChangesAsync();

        var savedLine = new Transaction
        {
            Date = date,
            Amount = -2.47m,
            Kind = TransactionKind.ReceiptLine,
            ParentTransactionId = parent.Id,
            CategoryId = harness.CategoryId,
            LedgerEntityId = harness.EntityId,
            AccountId = harness.AccountId,
            Notes = "Bananas",
            ExternalId = "R-1001",
            ImportBatchId = batch.Id,
            Merchant = "Walmart"
        };
        harness.Db.Transactions.Add(savedLine);
        first.Status = ImportItemStatus.Accepted;
        first.ResultingTransactionId = savedLine.Id;
        await harness.Db.SaveChangesAsync();
        harness.Db.ChangeTracker.Clear();

        var remaining = await harness.Db.ImportItems.SingleAsync(i => i.ImportBatchId == batch.Id && i.Status == ImportItemStatus.Pending);
        var result = await harness.Import.AcceptReceiptBatchAsync(
            new AcceptReceiptBatchRequest(
                batch.Id,
                harness.EntityId,
                harness.AccountId,
                [new ReceiptLineAcceptRequest(remaining.Id, remaining.Date, remaining.Amount, harness.CategoryId, remaining.Description)]),
            CancellationToken.None);

        Assert.Equal(ImportAcceptStatus.Accepted, result.Status);
        Assert.Equal(parent.Id, result.ReceiptTransaction?.Id);
        Assert.Equal(-7.46m, result.ReceiptTransaction?.Amount);
        Assert.Equal(1, await harness.Db.Transactions.CountAsync(t => t.Kind == TransactionKind.Receipt && t.ImportBatchId == batch.Id));
        Assert.Equal(2, await harness.Db.Transactions.CountAsync(t => t.ParentTransactionId == parent.Id));
    }

    private sealed class ReceiptAcceptHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ReceiptAcceptHarness(
            SqliteConnection connection,
            HomeLedgerDbContext db,
            CsvImportService import,
            int entityId,
            int accountId,
            int categoryId)
        {
            _connection = connection;
            Db = db;
            Import = import;
            EntityId = entityId;
            AccountId = accountId;
            CategoryId = categoryId;
        }

        public HomeLedgerDbContext Db { get; }
        public CsvImportService Import { get; }
        public int EntityId { get; }
        public int AccountId { get; }
        public int CategoryId { get; }

        public static async Task<ReceiptAcceptHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HomeLedgerDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new HomeLedgerDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var entity = new LedgerEntity { Name = "Household" };
            var group = new CategoryGroup { Name = "Daily Living", LedgerEntity = entity };
            var category = new Category { Name = "Groceries", CategoryGroup = group, LedgerEntity = entity };
            var account = new Account { Name = "Chequing", LedgerEntity = entity };
            db.Entities.Add(entity);
            db.CategoryGroups.Add(group);
            db.Categories.Add(category);
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var import = new CsvImportService(
                db,
                new StubCategorizer(),
                new ImportProfileService(db),
                new ImportSkipRuleMatcher(),
                new TransferPairMatcher(db),
                new NullImportRowClassifier(),
                new NullLlmClient(),
                Options.Create(new LlmSettings()),
                NullLogger<CsvImportService>.Instance);

            return new ReceiptAcceptHarness(connection, db, import, entity.Id, account.Id, category.Id);
        }

        public async Task<ImportBatch> CreatePendingReceiptBatchAsync(
            string merchant,
            string? externalId,
            params (DateOnly Date, decimal Amount, string Description)[] lines)
        {
            var batch = new ImportBatch
            {
                FileName = "receipt.jpg",
                AccountId = AccountId,
                LedgerEntityId = EntityId,
                Status = ImportBatchStatus.Reviewing,
                ImportKind = ImportKind.Receipt,
                Merchant = merchant,
                SourcePath = "receipt.jpg"
            };
            foreach (var line in lines)
            {
                batch.Items.Add(new ImportItem
                {
                    Date = line.Date,
                    Amount = line.Amount,
                    Description = line.Description,
                    ExternalId = externalId,
                    Merchant = merchant,
                    SourceFileName = "receipt.jpg",
                    SuggestedCategoryId = CategoryId,
                    SuggestedNotes = line.Description,
                    SuggestionSource = "llm",
                    Status = ImportItemStatus.Pending
                });
            }

            Db.ImportBatches.Add(batch);
            await Db.SaveChangesAsync();
            return batch;
        }

        public AcceptReceiptBatchRequest AcceptRequest(ImportBatch batch) =>
            new(
                batch.Id,
                EntityId,
                AccountId,
                batch.Items.Select(item => new ReceiptLineAcceptRequest(
                    item.Id,
                    item.Date,
                    item.Amount,
                    CategoryId,
                    item.Description)).ToList());

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubCategorizer : ITransactionCategorizer
    {
        public Task<CategorySuggestion> SuggestAsync(
            string description,
            decimal amount,
            IReadOnlyList<Category> categories,
            CancellationToken ct = default) =>
            Task.FromResult(new CategorySuggestion(categories.FirstOrDefault()?.Id, description, "stub"));
    }
}
