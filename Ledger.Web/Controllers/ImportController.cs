using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Export;
using Ledger.Infrastructure.Import;
using Ledger.Infrastructure.Services;
using Ledger.Web.Extensions;
using Ledger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Web.Controllers;

public class ImportController : Controller
{
    private readonly ICsvImportService _import;
    private readonly ILedgerExportService _export;
    private readonly IPdfStatementImportService _pdf;
    private readonly LedgerDbContext _db;
    private readonly ICategoryService _categories;

    public ImportController(
        ICsvImportService import,
        ILedgerExportService export,
        IPdfStatementImportService pdf,
        LedgerDbContext db,
        ICategoryService categories)
    {
        _import = import;
        _export = export;
        _pdf = pdf;
        _db = db;
        _categories = categories;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await PopulateLookupsAsync(null, ct);
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
            ModelState.AddModelError(nameof(model.File), "Please select a CSV or PDF file.");

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(null, ct);
            return View("Index", model);
        }

        await using var stream = model.File!.OpenReadStream();
        var (content, fileSha256) = await ImportFileFingerprint.ReadAndHashAsync(stream, ct);
        var isPdf = _pdf.IsPdfFile(model.File.FileName, model.File.ContentType);

        if (isPdf)
        {
            if (model.AccountId <= 0)
                ModelState.AddModelError(nameof(model.AccountId), "Please select an account for PDF statement imports.");
            if (model.LedgerEntityId <= 0)
                ModelState.AddModelError(nameof(model.LedgerEntityId), "Please select an entity for PDF statement imports.");

            if (!ModelState.IsValid)
            {
                await PopulateLookupsAsync(null, ct);
                return View("Index", model);
            }

            try
            {
                var rows = await _pdf.ExtractRowsAsync(content, ct);
                var pdfPriorImport = await _import.FindPriorImportAsync(fileSha256, model.File.Length, model.AccountId, ct);
                if (pdfPriorImport is not null)
                {
                    TempData[FlashMessage.WarningKey] =
                        $"This exact PDF was already imported on {pdfPriorImport.CompletedAt:yyyy/MM/dd}. Duplicate rows will be skipped automatically.";
                }

                var pdfBatch = await _import.CreateBatchFromRowsAsync(
                    rows,
                    model.File.FileName,
                    model.File.Length,
                    fileSha256,
                    model.AccountId,
                    model.LedgerEntityId,
                    model.AutoAccept,
                    ct);

                TempData[FlashMessage.SuccessKey] =
                    $"Extracted {rows.Count} transaction line(s) from the PDF using LLM vision.";

                if (model.AutoAccept)
                    return RedirectToAction(nameof(Complete), new { id = pdfBatch.Id });

                return RedirectToAction(nameof(Review), new { id = pdfBatch.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateLookupsAsync(null, ct);
                return View("Index", model);
            }
        }

        await using var headerStream = new MemoryStream(content);
        var headers = _export.ReadCsvHeaders(headerStream);

        if (_export.IsLedgerExport(headers))
        {
            try
            {
                await using var importStream = new MemoryStream(content);
                var result = await _export.ImportCsvAsync(importStream, ct);
                return View("~/Views/Export/ImportComplete.cshtml", result);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateLookupsAsync(null, ct);
                return View("Index", model);
            }
        }

        if (model.AccountId <= 0)
            ModelState.AddModelError(nameof(model.AccountId), "Please select an account for bank CSV imports.");
        if (model.LedgerEntityId <= 0)
            ModelState.AddModelError(nameof(model.LedgerEntityId), "Please select an entity for bank CSV imports.");

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(null, ct);
            return View("Index", model);
        }

        await using var csvStream = new MemoryStream(content);

        var priorImport = await _import.FindPriorImportAsync(fileSha256, model.File.Length, model.AccountId, ct);
        if (priorImport is not null)
        {
            TempData[FlashMessage.WarningKey] =
                $"This exact file was already imported on {priorImport.CompletedAt:yyyy/MM/dd}. Duplicate rows will be skipped automatically.";
        }

        var batch = await _import.CreateBatchAsync(
            csvStream,
            model.File.FileName,
            model.File.Length,
            fileSha256,
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

        await PopulateLookupsAsync(batch.LedgerEntityId, ct);

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
            await PopulateLookupsAsync(form.LedgerEntityId, ct);
            return View("Review", new ImportReviewModel
            {
                BatchId = batchId,
                Item = item,
                Form = form
            });
        }

        var result = await _import.AcceptItemAsync(new AcceptImportItemRequest(
            item.Id,
            form.Date,
            form.Amount,
            form.CategoryId,
            form.LedgerEntityId,
            form.AccountId,
            form.Notes), ct);

        switch (result.Status)
        {
            case ImportAcceptStatus.Accepted:
                TempData[FlashMessage.SuccessKey] = "Transaction saved.";
                break;
            case ImportAcceptStatus.SkippedDuplicate:
                TempData[FlashMessage.WarningKey] = result.Message;
                break;
            default:
                TempData[FlashMessage.ErrorKey] = result.Message ?? "This import row could not be saved.";
                break;
        }

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

    private async Task PopulateLookupsAsync(int? entityId, CancellationToken ct)
    {
        ViewBag.Categories = new SelectList(
            await _categories.GetSelectableCategoriesAsync(entityId, ct),
            "Id", "Name");
        ViewBag.Entities = new SelectList(
            await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct),
            "Id", "Name");
        ViewBag.Accounts = new SelectList(
            await _db.Accounts.AsNoTracking().Where(a => a.IsActive).ToListAsync(ct),
            "Id", "Name");
    }
}
