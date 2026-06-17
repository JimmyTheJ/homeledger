using Microsoft.AspNetCore.Mvc;

namespace HomeLedger.Web.Extensions;

public static class HtmxExtensions
{
    public static bool IsHtmxRequest(this HttpRequest request) =>
        request.Headers.ContainsKey("HX-Request");

    public static IActionResult HtmxOrView(this Controller controller, string partialViewName, object? model, string fullViewName)
    {
        if (controller.Request.IsHtmxRequest())
            return controller.PartialView(partialViewName, model);

        return controller.View(fullViewName, model);
    }

    public static IActionResult HtmxPartialOrRedirect(this Controller controller, string partialViewName, object? model, string redirectUrl)
    {
        if (controller.Request.IsHtmxRequest())
            return controller.PartialView(partialViewName, model);

        return controller.Redirect(redirectUrl);
    }
}
