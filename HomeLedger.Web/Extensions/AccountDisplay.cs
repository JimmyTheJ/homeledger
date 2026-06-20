using HomeLedger.Core.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HomeLedger.Web.Extensions;

public static class AccountDisplay
{
    public static string FormatLabel(Account account)
    {
        var kind = AccountKinds.Label(account.Kind);
        var institution = string.IsNullOrWhiteSpace(account.Institution) ? null : account.Institution;
        return institution is null
            ? $"{account.Name} ({kind})"
            : $"{account.Name} ({kind}) · {institution}";
    }

    public static SelectList ToSelectList(IEnumerable<Account> accounts) =>
        new(accounts.Select(a => new { a.Id, Label = FormatLabel(a) }), "Id", "Label");
}
