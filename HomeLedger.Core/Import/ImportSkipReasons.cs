namespace HomeLedger.Core.Import;

public static class ImportSkipReasons
{
    public const string DuplicateExternalId = "duplicate_external_id";
    public const string DuplicateTransaction = "duplicate_transaction";
    public const string DuplicatePriorImport = "duplicate_prior_import";
    public const string NoCategory = "no_category";
    public const string UserSkipped = "user_skipped";
    public const string CreditCardPayment = "credit_card_payment";
    public const string InternalTransfer = "internal_transfer";
    public const string InvestmentTransfer = "investment_transfer";
    public const string Reimbursement = "reimbursement";
    public const string LlmSuggestedSkip = "llm_suggested_skip";
    public const string PairedTransfer = "paired_transfer";
    public const string MatchedExistingReceipt = "matched_existing_receipt";

    public static string Describe(string? reason) => reason switch
    {
        DuplicateExternalId => "Already imported (matching bank transaction ID)",
        DuplicateTransaction => "Already imported (matching date, amount, and description)",
        DuplicatePriorImport => "Already imported from this same file previously",
        NoCategory => "No category could be suggested (auto-accept requires a category)",
        UserSkipped => "Skipped during review",
        CreditCardPayment => "Credit card payment (expenses are on the card account)",
        InternalTransfer => "Internal transfer between your accounts",
        InvestmentTransfer => "Investment or savings transfer",
        Reimbursement => "Insurance or expense reimbursement (not income)",
        LlmSuggestedSkip => "Suggested skip by AI classification",
        PairedTransfer => "Matching opposite transaction on another account",
        MatchedExistingReceipt => "Already covered by an imported receipt",
        null or "" => "Skipped",
        _ => reason
    };
}
