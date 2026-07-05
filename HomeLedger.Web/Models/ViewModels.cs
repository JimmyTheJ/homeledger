using HomeLedger.Core.Entities;
using HomeLedger.Infrastructure.Services;
using System.ComponentModel.DataAnnotations;

namespace HomeLedger.Web.Models;

public class TransactionFormModel
{
    public int? Id { get; set; }

    [Required]
    [DisplayFormat(DataFormatString = "{0:yyyy/MM/dd}", ApplyFormatInEditMode = true)]
    [DataType(DataType.Text)]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    public int LedgerEntityId { get; set; }

    public int? AccountId { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public static TransactionFormModel FromEntity(Transaction t) => new()
    {
        Id = t.Id,
        Date = t.Date,
        Amount = t.Amount,
        CategoryId = t.CategoryId,
        LedgerEntityId = t.LedgerEntityId,
        AccountId = t.AccountId,
        Notes = t.Notes
    };
}

public class ImportUploadModel
{
    public IFormFile? File { get; set; }

    public List<IFormFile> ReceiptImages { get; set; } = [];

    public int AccountId { get; set; }

    public int LedgerEntityId { get; set; }

    public bool AutoAccept { get; set; }
}

public class ImportReviewModel
{
    public string BatchId { get; set; } = string.Empty;
    public ImportBatch? Batch { get; set; }
    public ImportItem? Item { get; set; }
    public int PendingCount { get; set; }
    public int TotalCount { get; set; }
    public TransactionFormModel Form { get; set; } = new();
}

public class BudgetLimitFormModel
{
    public int? Id { get; set; }

    [Required]
    public int CategoryId { get; set; }

    public int? LedgerEntityId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal LimitAmount { get; set; }

    [Range(1, 100)]
    public decimal WarningThresholdPercent { get; set; } = 80;

    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;

    [DataType(DataType.Text)]
    [DisplayFormat(DataFormatString = "{0:yyyy/MM/dd}", ApplyFormatInEditMode = true)]
    public DateOnly? CustomStartDate { get; set; }

    [DataType(DataType.Text)]
    [DisplayFormat(DataFormatString = "{0:yyyy/MM/dd}", ApplyFormatInEditMode = true)]
    public DateOnly? CustomEndDate { get; set; }
}

public class YearlyReportViewModel
{
    public int Year { get; set; }
    public int? LedgerEntityId { get; set; }
    public YearlySummary Summary { get; set; } = null!;
    public IReadOnlyList<LedgerEntity> Entities { get; set; } = [];
}

public class MonthReportViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int? LedgerEntityId { get; set; }
    public IReadOnlyList<LedgerEntity> Entities { get; set; } = [];
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM");
}

public class CategoryGroupFormModel
{
    public int? Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsIncome { get; set; }
    public int SortOrder { get; set; }
    public int? LedgerEntityId { get; set; }
}

public class CategoryFormModel
{
    public int? Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int CategoryGroupId { get; set; }

    public int SortOrder { get; set; }
    public int? LedgerEntityId { get; set; }
}

public class DashboardViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int? LedgerEntityId { get; set; }
    public MonthlySummary Summary { get; set; } = null!;
    public IReadOnlyList<BudgetStatus> BudgetStatuses { get; set; } = [];
    public IReadOnlyList<LedgerEntity> Entities { get; set; } = [];
}
