namespace HomeLedger.Core.Import;

public static class ImportSkipReasons
{
    public const string DuplicateExternalId = "duplicate_external_id";
    public const string DuplicateTransaction = "duplicate_transaction";
    public const string DuplicatePriorImport = "duplicate_prior_import";
    public const string NoCategory = "no_category";
    public const string UserSkipped = "user_skipped";

    public static string Describe(string? reason) => reason switch
    {
        DuplicateExternalId => "Already imported (matching bank transaction ID)",
        DuplicateTransaction => "Already imported (matching date, amount, and description)",
        DuplicatePriorImport => "Already imported from this same file previously",
        NoCategory => "No category could be suggested (auto-accept requires a category)",
        UserSkipped => "Skipped during review",
        null or "" => "Skipped",
        _ => reason
    };
}
