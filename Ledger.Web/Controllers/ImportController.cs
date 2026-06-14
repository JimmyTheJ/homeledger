using Ledger.Infrastructure.Import;
using Ledger.Infrastructure.Data;
using Ledger.Web.Extensions;
using Ledger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Web.Controllers;

public class ImportController : Controller
{
    private readonly ICsvImportService _import;
    private readonly LedgerDbContext _db;

    public ImportController(ICsvImportService import, LedgerDbContext db)
    {
        _import = import;
        _db = db;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await PopulateLookupsAsync(ct);
        var batches = await _db.ImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .ToListAsync(ct);
        ViewBag.Batches = batches;
        return View(new ImportUploadModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(ImportUploadModel model, CancellationToken ct)
    {
        if (model.File is null || model.File.Length == 0)
            ModelState.AddModelError(nameof(model.File), "Please select a CSV file.");

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(ct);
            return View("Index", model);
        }

        await using var stream = model.File!.OpenReadStream();
        var batch = await _import.CreateBatchAsync(
            stream,
            model.File.FileName,
            model.AccountId,
            model.LedgerEntityId,
            model.AutoAccept,
            ct);

        if (model.AutoAccept)
            return RedirectToAction(nameof(Complete), new { id = batch.Id });

        return RedirectToAction(nameof(Review), new { id = batch.Id });
    }

    public async Task<IActionResult> Review(string id, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return NotFound();

        var item = await _import.GetNextPendingItemAsync(id, ct);
        var pending = await _db.ImportItems.CountAsync(i => i.ImportBatchId == id && i.Status == Core.Entities.ImportItemStatus.Pending, ct);
        var total = await _db.ImportItems.CountAsync(i => i.ImportBatchId == id, ct);

        if (item is null)
            return RedirectToAction(nameof(Complete), new { id });

        await PopulateLookupsAsync(ct);

        var vm = new ImportReviewModel
        {
            BatchId = id,
            Item = item,
            PendingCount = pending,
            TotalCount = total,
            Form = new TransactionFormModel
            {
                Date = item.Date,
                Amount = item.Amount,
                CategoryId = item.SuggestedCategoryId ?? 0,
                LedgerEntityId = batch.LedgerEntityId ?? 0,
                AccountId = batch.AccountId,
                Notes = item.SuggestedNotes ?? item.Description
            }
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(string batchId, TransactionFormModel form, CancellationToken ct)
    {
        var item = await _import.GetNextPendingItemAsync(batchId, ct);
        if (item is null)
            return RedirectToAction(nameof(Complete), new { id = batchId });

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(ct);
            return View("Review", new ImportReviewModel
            {
                BatchId = batchId,
                Item = item,
                Form = form
            });
        }

        await _import.AcceptItemAsync(new AcceptImportItemRequest(
            item.Id,
            form.Date,
            form.Amount,
            form.CategoryId,
            form.LedgerEntityId,
            form.AccountId,
            form.Notes), ct);

        var next = await _import.GetNextPendingItemAsync(batchId, ct);
        if (next is null)
            return RedirectToAction(nameof(Complete), new { id = batchId });

        return RedirectToAction(nameof(Review), new { id = batchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Skip(string batchId, int itemId, CancellationToken ct)
    {
        await _import.SkipItemAsync(itemId, ct);
        var next = await _import.GetNextPendingItemAsync(batchId, ct);
        if (next is null)
            return RedirectToAction(nameof(Complete), new { id = batchId });

        return RedirectToAction(nameof(Review), new { id = batchId });
    }

    public async Task<IActionResult> Complete(string id, CancellationToken ct)
    {
        var batch = await _db.ImportBatches
            .AsNoTracking()
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (batch is null) return NotFound();
        return View(batch);
    }

    private async Task PopulateLookupsAsync(CancellationToken ct)
    {
        ViewBag.Categories = new SelectList(
            await _db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(ct),
            "Id", "Name");
        ViewBag.Entities = new SelectList(
            await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct),
            "Id", "Name");
        ViewBag.Accounts = new SelectList(
            await _db.Accounts.AsNoTracking().Where(a => a.IsActive).ToListAsync(ct),
            "Id", "Name");
    }
}
