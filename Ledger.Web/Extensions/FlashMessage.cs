using Ledger.Core.Utilities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ledger.Web.Extensions;

public static class SelectListExtensions
{
    public static string FormatDateOption(this DateOnly date) => LedgerFormats.FormatDate(date);
}

public static class FlashMessage
{
    public const string SuccessKey = "Success";
    public const string ErrorKey = "Error";
    public const string WarningKey = "Warning";
}
