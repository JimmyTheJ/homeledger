using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Web.ViewComponents;

public class PendingReceiptImportsBadgeViewComponent : ViewComponent
{
    private readonly HomeLedgerDbContext _db;

    public PendingReceiptImportsBadgeViewComponent(HomeLedgerDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var pendingBatches = await _db.ImportBatches
            .AsNoTracking()
            .Where(b => b.Status == ImportBatchStatus.Reviewing
                && (b.ImportKind == ImportKind.Receipt || b.ImportKind == ImportKind.WatchedReceipt))
            .CountAsync();

        return View(pendingBatches);
    }
}
