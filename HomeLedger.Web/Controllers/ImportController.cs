using HomeLedger.Core.Configuration;
using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;
using HomeLedger.Infrastructure.Data;

using HomeLedger.Infrastructure.Export;

using HomeLedger.Infrastructure.Import;

using HomeLedger.Infrastructure.Llm;

using HomeLedger.Infrastructure.Services;

using HomeLedger.Web.Extensions;

using HomeLedger.Web.Models;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;



namespace HomeLedger.Web.Controllers;



public class ImportController : Controller

{

    private readonly ICsvImportService _import;

    private readonly IHomeLedgerExportService _export;

    private readonly IPdfStatementImportService _pdf;

    private readonly IReceiptImageImportService _receipts;

    private readonly HomeLedgerDbContext _db;

    private readonly ICategoryService _categories;

    private readonly ILlmHealthService _llmHealth;
    private readonly IReceiptInboxUploadService _inboxUpload;
    private readonly ReceiptInboxSettings _inboxSettings;
    private readonly IReceiptImportJobQueue _receiptJobs;

    public ImportController(

        ICsvImportService import,

        IHomeLedgerExportService export,

        IPdfStatementImportService pdf,

        IReceiptImageImportService receipts,

        HomeLedgerDbContext db,

        ICategoryService categories,

        ILlmHealthService llmHealth,
        IReceiptInboxUploadService inboxUpload,
        IOptions<ReceiptInboxSettings> inboxSettings,
        IReceiptImportJobQueue receiptJobs)

    {

        _import = import;

        _export = export;

        _pdf = pdf;

        _receipts = receipts;

        _db = db;

        _categories = categories;

        _llmHealth = llmHealth;
        _inboxUpload = inboxUpload;
        _inboxSettings = inboxSettings.Value;
        _receiptJobs = receiptJobs;

    }



    public async Task<IActionResult> Index(CancellationToken ct)

    {

        await PopulateIndexPageAsync(ct);

        return View(new ImportUploadModel());

    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ProcessingStatus()
    {
        if (HttpContext.Request.Headers.ContainsKey("HX-Request") && !_receiptJobs.HasActiveJobs)
            Response.Headers["HX-Refresh"] = "true";

        return PartialView("_ReceiptImportJobs", _receiptJobs.GetVisibleJobs());
    }



    [HttpPost]

    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ReceiptInboxSettings.DefaultMaxFileSizeBytes * ReceiptInboxSettings.DefaultMaxFilesPerUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = ReceiptInboxSettings.DefaultMaxFileSizeBytes * ReceiptInboxSettings.DefaultMaxFilesPerUpload)]

    public async Task<IActionResult> Upload(ImportUploadModel model, CancellationToken ct)

    {

        var receiptFiles = model.ReceiptImages.Where(f => f.Length > 0).ToList();

        var hasFile = model.File is { Length: > 0 };

        var hasReceipts = receiptFiles.Count > 0;



        if (!hasFile && !hasReceipts)

            ModelState.AddModelError(string.Empty, "Please select a CSV/PDF file or one or more receipt images.");



        if (hasFile && hasReceipts)

            ModelState.AddModelError(string.Empty, "Upload either a CSV/PDF file or receipt images, not both at once.");



        if (!ModelState.IsValid)

        {

            await PopulateIndexPageAsync(ct);

            return View("Index", model);

        }



        if (hasReceipts)

            return await UploadReceiptImagesAsync(model, receiptFiles, ct);



        return await UploadSingleFileAsync(model, ct);

    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ReceiptInboxSettings.DefaultMaxFileSizeBytes * ReceiptInboxSettings.DefaultMaxFilesPerUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = ReceiptInboxSettings.DefaultMaxFileSizeBytes * ReceiptInboxSettings.DefaultMaxFilesPerUpload)]
    public async Task<IActionResult> UploadToInbox(List<IFormFile> receiptImages, CancellationToken ct)
    {
        if (!_inboxUpload.IsReady)
        {
            TempData[FlashMessage.ErrorKey] = _inboxUpload.NotReadyReason ?? "Receipt inbox upload is not available.";
            return RedirectToAction(nameof(Index));
        }

        var files = receiptImages.Where(f => f.Length > 0).ToList();
        if (files.Count == 0)
        {
            TempData[FlashMessage.ErrorKey] = "Please select at least one receipt image.";
            return RedirectToAction(nameof(Index));
        }

        var uploads = new List<ReceiptInboxFileUpload>(files.Count);
        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            var (content, _) = await ImportFileFingerprint.ReadAndHashAsync(stream, ct);
            uploads.Add(new ReceiptInboxFileUpload(file.FileName, content, file.ContentType));
        }

        try
        {
            var result = await _inboxUpload.SaveFilesAsync(uploads, ct);
            var pollSeconds = Math.Max(5, _inboxSettings.PollIntervalSeconds);
            var message =
                $"{result.SavedCount} receipt image(s) saved to the inbox. They will be processed automatically within about {pollSeconds} seconds.";

            if (result.Rejected.Count > 0)
            {
                TempData[FlashMessage.WarningKey] =
                    message + " Some files were rejected: " + string.Join(" ", result.Rejected);
            }
            else
            {
                TempData[FlashMessage.SuccessKey] = message;
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData[FlashMessage.ErrorKey] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }



    private async Task<IActionResult> UploadReceiptImagesAsync(

        ImportUploadModel model,

        IReadOnlyList<IFormFile> receiptFiles,

        CancellationToken ct)

    {

        if (model.AccountId <= 0)

            ModelState.AddModelError(nameof(model.AccountId), "Please select an account for receipt imports.");

        if (model.LedgerEntityId <= 0)

            ModelState.AddModelError(nameof(model.LedgerEntityId), "Please select an entity for receipt imports.");



        if (!ModelState.IsValid)

        {

            await PopulateIndexPageAsync(ct);

            return View("Index", model);

        }

        var receiptFeature = _llmHealth.GetConfigurationStatus().Features
            .FirstOrDefault(f => f.Feature == "Receipt image import");
        if (receiptFeature is { EffectiveEnabled: false })
        {
            ModelState.AddModelError(string.Empty,
                receiptFeature.DisabledReason ?? "Receipt image import is not available.");
            await PopulateIndexPageAsync(ct);
            return View("Index", model);
        }

        var enqueue = new List<ReceiptImportEnqueueRequest>(receiptFiles.Count);

        foreach (var file in receiptFiles)

        {

            if (!_receipts.IsReceiptImageFile(file.FileName, file.ContentType))

            {

                ModelState.AddModelError(string.Empty, $"Unsupported receipt image: {file.FileName}");

                await PopulateIndexPageAsync(ct);

                return View("Index", model);

            }



            await using var stream = file.OpenReadStream();

            var (content, _) = await ImportFileFingerprint.ReadAndHashAsync(stream, ct);

            enqueue.Add(new ReceiptImportEnqueueRequest(
                file.FileName,
                content,
                file.ContentType,
                model.AccountId,
                model.LedgerEntityId,
                model.AutoAccept));

        }



        _receiptJobs.Enqueue(enqueue);

        TempData[FlashMessage.SuccessKey] =
            enqueue.Count == 1
                ? "Receipt uploaded. AI extraction is running in the background — watch progress below."
                : $"{enqueue.Count} receipts uploaded. AI extraction is running in the background — watch progress below. You can keep using the app while this finishes.";

        return RedirectToAction(nameof(Index));

    }



    private async Task<IActionResult> UploadSingleFileAsync(ImportUploadModel model, CancellationToken ct)

    {

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

                await PopulateIndexPageAsync(ct);

                return View("Index", model);

            }

            var pdfFeature = _llmHealth.GetConfigurationStatus().Features
                .FirstOrDefault(f => f.Feature == "PDF statement import");
            if (pdfFeature is { EffectiveEnabled: false })
            {
                ModelState.AddModelError(string.Empty,
                    pdfFeature.DisabledReason ?? "PDF statement import is not available.");
                await PopulateIndexPageAsync(ct);
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

                    pdfExtractedWithLlm: true,
                    importKind: ImportKind.Pdf,
                    ct: ct);



                TempData[FlashMessage.SuccessKey] =

                    $"Extracted {rows.Count} transaction line(s) from the PDF using LLM vision.";



                if (model.AutoAccept)

                    return RedirectToAction(nameof(Complete), new { id = pdfBatch.Id });



                return RedirectToAction(nameof(Review), new { id = pdfBatch.Id });

            }

            catch (InvalidOperationException ex)

            {

                ModelState.AddModelError(string.Empty, ex.Message);

                await PopulateIndexPageAsync(ct);

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

                await PopulateIndexPageAsync(ct);

                return View("Index", model);

            }

        }



        if (model.AccountId <= 0)

            ModelState.AddModelError(nameof(model.AccountId), "Please select an account for bank CSV imports.");

        if (model.LedgerEntityId <= 0)

            ModelState.AddModelError(nameof(model.LedgerEntityId), "Please select an entity for bank CSV imports.");



        if (!ModelState.IsValid)

        {

            await PopulateIndexPageAsync(ct);

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

        var batch = await _db.ImportBatches.AsNoTracking()

            .Include(b => b.Account)

            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (batch is null) return NotFound();



        var item = await _import.GetNextPendingItemAsync(id, ct);

        var pending = await _db.ImportItems.CountAsync(i => i.ImportBatchId == id && i.Status == Core.Entities.ImportItemStatus.Pending, ct);

        var total = await _db.ImportItems.CountAsync(i => i.ImportBatchId == id, ct);



        if (item is null)

            return RedirectToAction(nameof(Complete), new { id });



        await PopulateLookupsAsync(batch.LedgerEntityId, ct);

        if (batch.ImportKind is ImportKind.Receipt or ImportKind.WatchedReceipt)
        {
            var pendingItems = await _import.GetPendingItemsAsync(id, ct);
            var receiptVm = new ReceiptReviewModel
            {
                BatchId = id,
                Batch = batch,
                LedgerEntityId = batch.LedgerEntityId ?? 0,
                AccountId = batch.AccountId,
                Lines = pendingItems.Select(item => new ReceiptLineReviewModel
                {
                    ItemId = item.Id,
                    Description = item.Description,
                    Date = item.Date,
                    Amount = item.Amount,
                    CategoryId = item.SuggestedCategoryId ?? 0,
                    Notes = item.SuggestedNotes ?? item.Description,
                    SuggestedCategoryName = item.SuggestedCategory?.Name,
                    SuggestionSource = item.SuggestionSource,
                    Quantity = item.Quantity,
                    QuantityUnit = item.QuantityUnit,
                    UnitPrice = item.UnitPrice
                }).ToList()
            };

            return View("ReceiptReview", receiptVm);
        }



        var vm = new ImportReviewModel

        {

            BatchId = id,

            Batch = batch,

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

            var batch = await _db.ImportBatches.AsNoTracking()

                .Include(b => b.Account)

                .FirstOrDefaultAsync(b => b.Id == batchId, ct);

            await PopulateLookupsAsync(form.LedgerEntityId, ct);

            var pending = await _db.ImportItems.CountAsync(i => i.ImportBatchId == batchId && i.Status == Core.Entities.ImportItemStatus.Pending, ct);

            var total = await _db.ImportItems.CountAsync(i => i.ImportBatchId == batchId, ct);

            return View("Review", new ImportReviewModel

            {

                BatchId = batchId,

                Batch = batch,

                Item = item,

                PendingCount = pending,

                TotalCount = total,

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
    public async Task<IActionResult> AcceptReceipt(string batchId, int ledgerEntityId, int? accountId, List<ReceiptLineReviewModel> lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            TempData[FlashMessage.ErrorKey] = "Select at least one receipt line item to import.";
            return RedirectToAction(nameof(Review), new { id = batchId });
        }

        if (lines.Any(l => l.CategoryId <= 0))
        {
            TempData[FlashMessage.ErrorKey] = "Each imported line needs a category.";
            return RedirectToAction(nameof(Review), new { id = batchId });
        }

        var acceptLines = lines.Select(l => new ReceiptLineAcceptRequest(
            l.ItemId,
            l.Date,
            l.Amount,
            l.CategoryId,
            l.Notes,
            l.Quantity,
            l.QuantityUnit,
            l.UnitPrice)).ToList();

        var result = await _import.AcceptReceiptBatchAsync(new AcceptReceiptBatchRequest(
            batchId,
            ledgerEntityId,
            accountId,
            acceptLines), ct);

        switch (result.Status)
        {
            case ImportAcceptStatus.Accepted:
                var message = $"Receipt saved with {acceptLines.Count} categorized line item(s).";
                if (result.SupersededTransactionIds.Count > 0)
                {
                    message += $" Replaced {result.SupersededTransactionIds.Count} existing transaction(s) " +
                        $"with more detailed receipt data (IDs: {string.Join(", ", result.SupersededTransactionIds)}). " +
                        "The replaced entries remain visible when you enable “Show superseded” on Transactions.";
                }

                TempData[FlashMessage.SuccessKey] = message;
                return RedirectToAction(nameof(Complete), new { id = batchId });
            default:
                TempData[FlashMessage.ErrorKey] = result.Message ?? "This receipt could not be saved.";
                return RedirectToAction(nameof(Review), new { id = batchId });
        }
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

            .Include(b => b.Account)

            .FirstOrDefaultAsync(b => b.Id == id, ct);



        if (batch is null) return NotFound();

        return View(batch);

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Delete(string id, CancellationToken ct)

    {

        var result = await _import.DeleteIncompleteBatchAsync(id, ct);

        switch (result.Status)

        {

            case DeleteBatchStatus.Deleted:

                TempData[FlashMessage.SuccessKey] = result.TransactionsKept > 0

                    ? $"Import deleted. {result.TransactionsKept} transaction(s) already saved were kept in your ledger."

                    : "Import deleted.";

                break;

            case DeleteBatchStatus.AlreadyCompleted:

                TempData[FlashMessage.WarningKey] = "Completed imports cannot be deleted.";

                break;

            default:

                TempData[FlashMessage.ErrorKey] = "Import not found.";

                break;

        }



        return RedirectToAction(nameof(Index));

    }



    private async Task PopulateIndexPageAsync(CancellationToken ct)
    {
        await PopulateLookupsAsync(null, ct);

        ViewBag.Batches = await _db.ImportBatches
            .AsNoTracking()
            .Include(b => b.Account)
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        ViewBag.PendingReceiptBatches = await _db.ImportBatches
            .AsNoTracking()
            .Include(b => b.Account)
            .Include(b => b.Items)
            .Where(b => b.Status == Core.Entities.ImportBatchStatus.Reviewing
                && (b.ImportKind == ImportKind.Receipt || b.ImportKind == ImportKind.WatchedReceipt))
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        ViewBag.LlmHealth = _llmHealth.GetConfigurationStatus();
        ViewBag.ReceiptInbox = _inboxSettings;
        ViewBag.ReceiptInboxReady = _inboxUpload.IsReady;
        ViewBag.ReceiptImportJobs = _receiptJobs.GetVisibleJobs();
    }

    private async Task PopulateLookupsAsync(int? entityId, CancellationToken ct)

    {

        ViewBag.Categories = new SelectList(

            await _categories.GetSelectableCategoriesAsync(entityId, ct),

            "Id", "Name");

        ViewBag.Entities = new SelectList(

            await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct),

            "Id", "Name");

        ViewBag.Accounts = AccountDisplay.ToSelectList(

            await _db.Accounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(ct));

    }

}


