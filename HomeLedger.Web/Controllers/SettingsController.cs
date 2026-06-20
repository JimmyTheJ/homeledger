using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Llm;
using Microsoft.AspNetCore.Mvc;

namespace HomeLedger.Web.Controllers;

public class SettingsController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly ILlmHealthService _llmHealth;

    public SettingsController(IConfiguration configuration, ILlmHealthService llmHealth)
    {
        _configuration = configuration;
        _llmHealth = llmHealth;
    }

    public async Task<IActionResult> Index(bool check = false, CancellationToken ct = default)
    {
        var llm = _configuration.GetSection(LlmSettings.SectionName).Get<LlmSettings>() ?? new();
        ViewBag.LlmHealth = check
            ? await _llmHealth.CheckHealthAsync(ct)
            : _llmHealth.GetConfigurationStatus();
        return View(llm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CheckLlm(CancellationToken ct) =>
        Index(check: true, ct);
}
