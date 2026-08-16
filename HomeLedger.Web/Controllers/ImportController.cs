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
    private const string AccountCookie = "hl-import-account";
    private const string EntityCookie = "hl-import-entity";

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

    public async Task<IActionResult> Index(string? receipt, CancellationToken ct)
    {
        var model = new ImportUploadModel();
        await PopulateIndexPageAsync(receipt, model, ct);
        return View(model);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ReceiptQueue(string? receipt, CancellationToken ct)
    {
        var queue = await LoadReceiptQueueAsync(receipt, ct);
        if (queue.Current is not null)
            await PopulateLookupsAsync(queue.Current.LedgerEntityId, ct);

        return PartialView("_ReceiptQueue", queue);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ProcessingStatus(CancellationToken ct)
    {
        var awaitingReview = await PendingReceiptBatchIdsAsync(ct);
        var jobs = ReceiptImportJobQueue.WithoutSavedReceipts(_receiptJobs.GetVisibleJobs(), awaitingReview);
        if (Request.Headers.ContainsKey("HX-Request") && !_receiptJobs.HasActiveJobs)
            Response.Headers["HX-Trigger"] = "receipt-queue-check";

        return PartialView("_ReceiptImportJobs", jobs);
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
            await PopulateIndexPageAsync(null, model, ct);
            return View("Index", model);
        }

        RememberImportTargets(model.AccountId, model.LedgerEntityId);

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
                $"{result.SavedCount} receipt image(s) queued. Extraction usually starts within {pollSeconds}s.";

            if (result.Rejected.Count > 0)
            {
                TempData[FlashMessage.WarningKey] =
                    message + " Rejected: " + string.Join(" ", result.Rejected);
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
            await PopulateIndexPageAsync(null, model, ct);
            return View("Index", model);
        }

        var receiptFeature = _llmHealth.GetConfigurationStatus().Features
            .FirstOrDefault(f => f.Feature == "Receipt image import");
        if (receiptFeature is { EffectiveEnabled: false })
        {
            ModelState.AddModelError(string.Empty,
                receiptFeature.DisabledReason ?? "Receipt image import is not available.");
            await PopulateIndexPageAsync(null, model, ct);
            return View("Index", model);
        }

        var enqueue = new List<ReceiptImportEnqueueRequest>(receiptFiles.Count);
        foreach (var file in receiptFiles)
        {
            if (!_receipts.IsReceiptImageFile(file.FileName, file.ContentType))
            {
                ModelState.AddModelError(string.Empty, $"Unsupported receipt image: {file.FileName}");
                await PopulateIndexPageAsync(null, model, ct);
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
                ? "Receipt queued. Extraction runs in the background."
                : $"{enqueue.Count} receipts queued. Extraction runs in the background.";

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
                await PopulateIndexPageAsync(null, model, ct);
                return View("Index", model);
            }

            var pdfFeature = _llmHealth.GetConfigurationStatus().Features
                .FirstOrDefault(f => f.Feature == "PDF statement import");
            if (pdfFeature is { EffectiveEnabled: false })
            {
                ModelState.AddModelError(string.Empty,
                    pdfFeature.DisabledReason ?? "PDF statement import is not available.");
                await PopulateIndexPageAsync(null, model, ct);
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
                await PopulateIndexPageAsync(null, model, ct);
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
                await PopulateIndexPageAsync(null, model, ct);
                return View("Index", model);
            }
        }

        if (model.AccountId <= 0)
            ModelState.AddModelError(nameof(model.AccountId), "Please select an account for bank CSV imports.");

        if (model.LedgerEntityId <= 0)
            ModelState.AddModelError(nameof(model.LedgerEntityId), "Please select an entity for bank CSV imports.");

        if (!ModelState.IsValid)
        {
            await PopulateIndexPageAsync(null, model, ct);
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
        var kind = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => (ImportKind?)b.ImportKind)
            .FirstOrDefaultAsync(ct);

        if (kind is null)
            return NotFound();

        if (kind is ImportKind.Receipt or ImportKind.WatchedReceipt)
            return RedirectToAction(nameof(Index), new { receipt = id });

        var vm = await BuildCsvReviewAsync(id, ct);
        if (vm is null)
            return RedirectToAction(nameof(Complete), new { id });

        if (IsHtmx)
            return PartialView("_ImportReviewPanel", vm);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(string batchId, TransactionFormModel form, CancellationToken ct)
    {
        var item = await _import.GetNextPendingItemAsync(batchId, ct);
        if (item is null)
            return FinishCsvReview(batchId);

        if (!ModelState.IsValid)
        {
            var vm = await BuildCsvReviewAsync(batchId, ct, item, form);
            if (vm is null)
                return FinishCsvReview(batchId);

            return IsHtmx ? PartialView("_ImportReviewPanel", vm) : View("Review", vm);
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
            return FinishCsvReview(batchId);

        if (IsHtmx)
        {
            var vm = await BuildCsvReviewAsync(batchId, ct, next);
            return vm is null
                ? FinishCsvReview(batchId)
                : PartialView("_ImportReviewPanel", vm);
        }

        return RedirectToAction(nameof(Review), new { id = batchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptReceipt(
        string batchId,
        int ledgerEntityId,
        int? accountId,
        List<ReceiptLineReviewModel> lines,
        CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            TempData[FlashMessage.ErrorKey] = "Select at least one receipt line item to import.";
            return RedirectToAction(nameof(Index), new { receipt = batchId });
        }

        if (lines.Any(l => l.CategoryId <= 0))
        {
            TempData[FlashMessage.ErrorKey] = "Each imported line needs a category.";
            return RedirectToAction(nameof(Index), new { receipt = batchId });
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
                var merchant = result.ReceiptTransaction?.Merchant;
                var message = string.IsNullOrWhiteSpace(merchant)
                    ? $"Saved {acceptLines.Count} line(s)."
                    : $"Saved {merchant} ({acceptLines.Count} line(s)).";
                if (result.SupersededTransactionIds.Count > 0)
                {
                    message += $" Replaced {result.SupersededTransactionIds.Count} matching bank transaction(s).";
                }

                TempData[FlashMessage.SuccessKey] = message;
                var nextId = await NextPendingReceiptBatchIdAsync(batchId, ct);
                return nextId is null
                    ? RedirectToAction(nameof(Index))
                    : RedirectToAction(nameof(Index), new { receipt = nextId });
            default:
                TempData[FlashMessage.ErrorKey] = result.Message ?? "This receipt could not be saved.";
                return RedirectToAction(nameof(Index), new { receipt = batchId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Skip(string batchId, int itemId, CancellationToken ct)
    {
        await _import.SkipItemAsync(itemId, ct);
        var next = await _import.GetNextPendingItemAsync(batchId, ct);
        if (next is null)
            return FinishCsvReview(batchId);

        if (IsHtmx)
        {
            var vm = await BuildCsvReviewAsync(batchId, ct, next);
            return vm is null
                ? FinishCsvReview(batchId)
                : PartialView("_ImportReviewPanel", vm);
        }

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

    private bool IsHtmx => Request.Headers.ContainsKey("HX-Request");

    private IActionResult FinishCsvReview(string batchId)
    {
        if (IsHtmx)
        {
            Response.Headers["HX-Redirect"] = Url.Action(nameof(Complete), new { id = batchId }) ?? "/Import";
            return NoContent();
        }

        return RedirectToAction(nameof(Complete), new { id = batchId });
    }

    private async Task<ImportReviewModel?> BuildCsvReviewAsync(
        string batchId,
        CancellationToken ct,
        ImportItem? item = null,
        TransactionFormModel? form = null)
    {
        var batch = await _db.ImportBatches.AsNoTracking()
            .Include(b => b.Account)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
            return null;

        item ??= await _import.GetNextPendingItemAsync(batchId, ct);
        if (item is null)
            return null;

        var pending = await _db.ImportItems.CountAsync(
            i => i.ImportBatchId == batchId && i.Status == ImportItemStatus.Pending, ct);
        var total = await _db.ImportItems.CountAsync(i => i.ImportBatchId == batchId, ct);
        await PopulateLookupsAsync(form?.LedgerEntityId ?? batch.LedgerEntityId, ct);

        return new ImportReviewModel
        {
            BatchId = batchId,
            Batch = batch,
            Item = item,
            PendingCount = pending,
            TotalCount = total,
            Form = form ?? new TransactionFormModel
            {
                Date = item.Date,
                Amount = item.Amount,
                CategoryId = item.SuggestedCategoryId ?? 0,
                LedgerEntityId = batch.LedgerEntityId ?? 0,
                AccountId = batch.AccountId,
                Notes = item.SuggestedNotes ?? item.Description
            }
        };
    }

    private async Task PopulateIndexPageAsync(string? receiptId, ImportUploadModel model, CancellationToken ct)
    {
        await PopulateLookupsAsync(null, ct);
        ApplyImportDefaults(model);

        ViewBag.Batches = await _db.ImportBatches
            .AsNoTracking()
            .Include(b => b.Account)
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        var queue = await LoadReceiptQueueAsync(receiptId, ct);
        if (queue.Current is not null)
            await PopulateLookupsAsync(queue.Current.LedgerEntityId, ct);

        ViewBag.ReceiptQueue = queue;
        ViewBag.LlmHealth = _llmHealth.GetConfigurationStatus();
        ViewBag.ReceiptInbox = _inboxSettings;
        ViewBag.ReceiptInboxReady = _inboxUpload.IsReady;
        ViewBag.ReceiptImportJobs = await GetDisplayReceiptJobsAsync(ct);
    }

    private async Task<IReadOnlyList<ReceiptImportJobSnapshot>> GetDisplayReceiptJobsAsync(CancellationToken ct)
    {
        var jobs = _receiptJobs.GetVisibleJobs();
        var completedBatchIds = jobs
            .Where(j => j.Status == ReceiptImportJobStatus.Completed
                && !string.IsNullOrWhiteSpace(j.ResultBatchId))
            .Select(j => j.ResultBatchId!)
            .Distinct()
            .ToList();

        HashSet<string> awaitingReview = new(StringComparer.Ordinal);
        if (completedBatchIds.Count > 0)
        {
            var pending = await _db.ImportBatches
                .AsNoTracking()
                .Where(b => completedBatchIds.Contains(b.Id)
                    && b.Status == ImportBatchStatus.Reviewing
                    && b.Items.Any(i => i.Status == ImportItemStatus.Pending))
                .Select(b => b.Id)
                .ToListAsync(ct);
            awaitingReview = pending.ToHashSet(StringComparer.Ordinal);
        }

        return ReceiptImportJobQueue.WithoutSavedReceipts(jobs, awaitingReview);
    }

    private async Task<ReceiptQueueModel> LoadReceiptQueueAsync(string? receiptId, CancellationToken ct)
    {
        var pending = await _db.ImportBatches
            .AsNoTracking()
            .Include(b => b.Account)
            .Include(b => b.Items)
                .ThenInclude(i => i.SuggestedCategory)
            .Where(b => b.Status == ImportBatchStatus.Reviewing
                && (b.ImportKind == ImportKind.Receipt || b.ImportKind == ImportKind.WatchedReceipt)
                && b.Items.Any(i => i.Status == ImportItemStatus.Pending))
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(ct);

        var current = pending.FirstOrDefault(b => b.Id == receiptId)
            ?? pending.FirstOrDefault();

        return new ReceiptQueueModel
        {
            TotalPending = pending.Count,
            Current = current is null ? null : ToReceiptReview(current),
            Waiting = pending
                .Where(b => current is null || b.Id != current.Id)
                .Select(b => new ReceiptQueueItemModel
                {
                    BatchId = b.Id,
                    Label = string.IsNullOrWhiteSpace(b.Merchant) ? b.FileName : b.Merchant,
                    LineCount = b.Items.Count(i => i.Status == ImportItemStatus.Pending)
                })
                .ToList()
        };
    }

    private static ReceiptReviewModel ToReceiptReview(ImportBatch batch)
    {
        var pendingItems = batch.Items
            .Where(i => i.Status == ImportItemStatus.Pending)
            .OrderBy(i => i.Id)
            .ToList();

        return new ReceiptReviewModel
        {
            BatchId = batch.Id,
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
    }

    private async Task<HashSet<string>> PendingReceiptBatchIdsAsync(CancellationToken ct)
    {
        var ids = await _db.ImportBatches
            .AsNoTracking()
            .Where(b => b.Status == ImportBatchStatus.Reviewing
                && (b.ImportKind == ImportKind.Receipt || b.ImportKind == ImportKind.WatchedReceipt)
                && b.Items.Any(i => i.Status == ImportItemStatus.Pending))
            .Select(b => b.Id)
            .ToListAsync(ct);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<string?> NextPendingReceiptBatchIdAsync(string exceptBatchId, CancellationToken ct) =>
        await _db.ImportBatches
            .AsNoTracking()
            .Where(b => b.Id != exceptBatchId
                && b.Status == ImportBatchStatus.Reviewing
                && (b.ImportKind == ImportKind.Receipt || b.ImportKind == ImportKind.WatchedReceipt)
                && b.Items.Any(i => i.Status == ImportItemStatus.Pending))
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .FirstOrDefaultAsync(ct);

    private void ApplyImportDefaults(ImportUploadModel model)
    {
        if (model.AccountId <= 0
            && int.TryParse(Request.Cookies[AccountCookie], out var cookieAccount)
            && cookieAccount > 0)
        {
            model.AccountId = cookieAccount;
        }
        else if (model.AccountId <= 0 && _inboxSettings.AccountId > 0)
        {
            model.AccountId = _inboxSettings.AccountId;
        }
        else if (model.AccountId <= 0 && ViewBag.Accounts is SelectList accounts)
        {
            var accountOptions = accounts.Where(o => !string.IsNullOrEmpty(o.Value)).ToList();
            if (accountOptions.Count == 1 && int.TryParse(accountOptions[0].Value, out var onlyAccount))
                model.AccountId = onlyAccount;
        }

        if (model.LedgerEntityId <= 0
            && int.TryParse(Request.Cookies[EntityCookie], out var cookieEntity)
            && cookieEntity > 0)
        {
            model.LedgerEntityId = cookieEntity;
        }
        else if (model.LedgerEntityId <= 0 && _inboxSettings.LedgerEntityId > 0)
        {
            model.LedgerEntityId = _inboxSettings.LedgerEntityId;
        }
        else if (model.LedgerEntityId <= 0 && ViewBag.Entities is SelectList entities)
        {
            var entityOptions = entities.Where(o => !string.IsNullOrEmpty(o.Value)).ToList();
            if (entityOptions.Count == 1 && int.TryParse(entityOptions[0].Value, out var onlyEntity))
                model.LedgerEntityId = onlyEntity;
        }
    }

    private void RememberImportTargets(int accountId, int ledgerEntityId)
    {
        var options = new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax
        };

        if (accountId > 0)
            Response.Cookies.Append(AccountCookie, accountId.ToString(), options);
        if (ledgerEntityId > 0)
            Response.Cookies.Append(EntityCookie, ledgerEntityId.ToString(), options);
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
