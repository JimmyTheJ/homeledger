using Ledger.Infrastructure.Export;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Web.Controllers;

public class ExportController : Controller
{
    private readonly ILedgerExportService _export;

    public ExportController(ILedgerExportService export) => _export = export;

    public IActionResult Index() => View();

    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var bytes = await _export.ExportCsvAsync(ct);
        var fileName = $"ledger-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }
}
