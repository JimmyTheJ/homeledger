namespace HomeLedger.Core.Entities;

public enum AccountKind
{
    Chequing = 0,
    Savings = 1,
    Visa = 2,
    Mastercard = 3,
    Amex = 4,
    CreditCard = 5,
    Investment = 6,
    Other = 99
}

public static class AccountKinds
{
    public static string Label(AccountKind kind) => kind switch
    {
        AccountKind.Chequing => "Chequing",
        AccountKind.Savings => "Savings",
        AccountKind.Visa => "Visa",
        AccountKind.Mastercard => "Mastercard",
        AccountKind.Amex => "Amex",
        AccountKind.CreditCard => "Credit card",
        AccountKind.Investment => "Investment",
        AccountKind.Other => "Other",
        _ => kind.ToString()
    };

    public static bool IsCreditCard(AccountKind kind) => kind is
        AccountKind.Visa or AccountKind.Mastercard or AccountKind.Amex or AccountKind.CreditCard;
}
