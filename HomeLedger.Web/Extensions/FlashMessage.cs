using HomeLedger.Core.Utilities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HomeLedger.Web.Extensions;

public static class SelectListExtensions
{
    public static string FormatDateOption(this DateOnly date) => HomeLedgerFormats.FormatDate(date);
}

public static class FlashMessage
{
    public const string SuccessKey = "Success";
    public const string ErrorKey = "Error";
    public const string WarningKey = "Warning";
}
