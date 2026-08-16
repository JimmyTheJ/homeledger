using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Llm;
using HomeLedger.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HomeLedger.Web.Controllers;

public class SettingsController : Controller
{
    private readonly IOptionsMonitor<LlmSettings> _llm;
    private readonly ILlmSettingsOverlayStore _overlay;
    private readonly IConfiguration _configuration;
    private readonly ILlmHealthService _llmHealth;

    public SettingsController(
        IOptionsMonitor<LlmSettings> llm,
        ILlmSettingsOverlayStore overlay,
        IConfiguration configuration,
        ILlmHealthService llmHealth)
    {
        _llm = llm;
        _overlay = overlay;
        _configuration = configuration;
        _llmHealth = llmHealth;
    }

    public async Task<IActionResult> Index(bool check = false, CancellationToken ct = default)
    {
        var llm = _llm.CurrentValue;
        BindSettingsPage(llm, check ? await _llmHealth.CheckHealthAsync(ct) : _llmHealth.GetConfigurationStatus());
        return View("Index", llm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CheckLlm(CancellationToken ct) =>
        Index(check: true, ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLlmRuntime(LlmRuntimeSettings model, CancellationToken ct)
    {
        await _overlay.SaveAsync(model, ct);
        TempData[FlashMessage.SuccessKey] =
            "Vision settings saved. New receipt and statement imports use them immediately.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetLlmRuntime(CancellationToken ct)
    {
        await _overlay.ClearAsync(ct);
        TempData[FlashMessage.SuccessKey] =
            "Vision settings reset to .env / appsettings defaults.";
        return RedirectToAction(nameof(Index));
    }

    private void BindSettingsPage(LlmSettings llm, LlmHealthReport health)
    {
        var inbox = _configuration.GetSection(ReceiptInboxSettings.SectionName).Get<ReceiptInboxSettings>() ?? new();
        var database = _configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new();
        ViewBag.DatabaseProvider = database.ResolvedProvider;
        ViewBag.ReceiptInbox = inbox;
        ViewBag.LlmHealth = health;
        ViewBag.Runtime = LlmRuntimeSettings.From(llm);
        ViewBag.OverlayActive = _overlay.Exists;
    }
}
