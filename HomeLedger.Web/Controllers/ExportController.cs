using HomeLedger.Infrastructure.Export;
using Microsoft.AspNetCore.Mvc;

namespace HomeLedger.Web.Controllers;

public class ExportController : Controller
{
    private readonly IHomeLedgerExportService _export;

    public ExportController(IHomeLedgerExportService export) => _export = export;

    public IActionResult Index() => View();

    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var bytes = await _export.ExportCsvAsync(ct);
        var fileName = $"ledger-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }
}
