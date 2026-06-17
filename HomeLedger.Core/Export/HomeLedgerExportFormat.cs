namespace HomeLedger.Core.Export;

public static class HomeLedgerExportFormat
{
    public const string Version = "homeledger-export-v1";

    public const string RecordMeta = "meta";
    public const string RecordEntity = "entity";
    public const string RecordAccount = "account";
    public const string RecordCategoryGroup = "category_group";
    public const string RecordCategory = "category";
    public const string RecordBudget = "budget";
    public const string RecordTransaction = "transaction";

    public static readonly string[] Headers =
    [
        "RecordType",
        "EntityName",
        "AccountName",
        "CategoryGroupName",
        "CategoryName",
        "Date",
        "Amount",
        "Notes",
        "ExternalId",
        "ImportBatchId",
        "Institution",
        "AccountNumberLast4",
        "Color",
        "IsActive",
        "IsIncome",
        "SortOrder",
        "LimitAmount",
        "WarningThresholdPercent",
        "Period",
        "CustomStartDate",
        "CustomEndDate",
        "CreatedAt",
        "UpdatedAt"
    ];
}
