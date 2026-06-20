using HomeLedger.Core.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace HomeLedger.Web.Controllers;

public class SettingsController : Controller
{
    private readonly IConfiguration _configuration;

    public SettingsController(IConfiguration configuration) => _configuration = configuration;

    public IActionResult Index()
    {
        var llm = _configuration.GetSection(LlmSettings.SectionName).Get<LlmSettings>() ?? new();
        return View(llm);
    }
}
