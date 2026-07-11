using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Services;
using HomeLedger.Web.Extensions;
using HomeLedger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HomeLedger.Web.Controllers;

public class TransactionsController : Controller
{
    private readonly HomeLedgerDbContext _db;
    private readonly IReportService _reports;
    private readonly ICategoryService _categories;

    public TransactionsController(HomeLedgerDbContext db, IReportService reports, ICategoryService categories)
    {
        _db = db;
        _reports = reports;
        _categories = categories;
    }

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? categoryId, int? entityId, bool showSuperseded = false, CancellationToken ct = default)
    {
        var transactions = await _reports.GetTransactionsAsync(from, to, categoryId, entityId, showSuperseded, ct);
        await PopulateLookupsAsync(entityId, ct);
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.CategoryId = categoryId;
        ViewBag.EntityId = entityId;
        ViewBag.ShowSuperseded = showSuperseded;
        return View(transactions);
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateLookupsAsync(null, ct);
        return View(new TransactionFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransactionFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(model.LedgerEntityId, ct);
            return View(model);
        }

        _db.Transactions.Add(new Core.Entities.Transaction
        {
            Date = model.Date,
            Amount = model.Amount,
            Kind = Core.Entities.TransactionKind.Standard,
            CategoryId = model.CategoryId,
            LedgerEntityId = model.LedgerEntityId,
            AccountId = model.AccountId,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim()
        });
        await _db.SaveChangesAsync(ct);
        TempData[FlashMessage.SuccessKey] = "Transaction saved.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var transaction = await _db.Transactions.FindAsync([id], ct);
        if (transaction is null || transaction.Kind != Core.Entities.TransactionKind.Standard) return NotFound();

        await PopulateLookupsAsync(transaction.LedgerEntityId, ct);
        return View(TransactionFormModel.FromEntity(transaction));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TransactionFormModel model, CancellationToken ct)
    {
        var transaction = await _db.Transactions.FindAsync([id], ct);
        if (transaction is null || transaction.Kind != Core.Entities.TransactionKind.Standard) return NotFound();

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(model.LedgerEntityId, ct);
            return View(model);
        }

        transaction.Date = model.Date;
        transaction.Amount = model.Amount;
        transaction.CategoryId = model.CategoryId;
        transaction.LedgerEntityId = model.LedgerEntityId;
        transaction.AccountId = model.AccountId;
        transaction.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
        transaction.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        TempData[FlashMessage.SuccessKey] = "Transaction updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var transaction = await _db.Transactions.FindAsync([id], ct);
        if (transaction is not null)
        {
            _db.Transactions.Remove(transaction);
            await _db.SaveChangesAsync(ct);
            TempData[FlashMessage.SuccessKey] = "Transaction deleted.";
        }

        if (Request.IsHtmxRequest())
            return Content(string.Empty);

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateLookupsAsync(int? entityId = null, CancellationToken ct = default)
    {
        var categoryList = await _categories.GetSelectableCategoriesAsync(entityId, ct);
        ViewBag.Categories = new SelectList(categoryList, "Id", "Name");
        ViewBag.Entities = new SelectList(
            await _db.Entities.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct),
            "Id", "Name");
        ViewBag.Accounts = new SelectList(
            await _db.Accounts.AsNoTracking().Where(a => a.IsActive).ToListAsync(ct),
            "Id", "Name");
    }
}
