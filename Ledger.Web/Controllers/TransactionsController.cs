using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Services;
using Ledger.Web.Extensions;
using Ledger.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Web.Controllers;

public class TransactionsController : Controller
{
    private readonly LedgerDbContext _db;
    private readonly IReportService _reports;

    public TransactionsController(LedgerDbContext db, IReportService reports)
    {
        _db = db;
        _reports = reports;
    }

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? categoryId, int? entityId, CancellationToken ct)
    {
        var transactions = await _reports.GetTransactionsAsync(from, to, categoryId, entityId, ct);
        await PopulateLookupsAsync(ct);
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.CategoryId = categoryId;
        ViewBag.EntityId = entityId;
        return View(transactions);
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateLookupsAsync(ct);
        return View(new TransactionFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransactionFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(ct);
            return View(model);
        }

        _db.Transactions.Add(new Core.Entities.Transaction
        {
            Date = model.Date,
            Amount = model.Amount,
            CategoryId = model.CategoryId,
            LedgerEntityId = model.LedgerEntityId,
            AccountId = model.AccountId,
            Notes = model.Notes
        });
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var transaction = await _db.Transactions.FindAsync([id], ct);
        if (transaction is null) return NotFound();

        await PopulateLookupsAsync(ct);
        return View(TransactionFormModel.FromEntity(transaction));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TransactionFormModel model, CancellationToken ct)
    {
        var transaction = await _db.Transactions.FindAsync([id], ct);
        if (transaction is null) return NotFound();

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(ct);
            return View(model);
        }

        transaction.Date = model.Date;
        transaction.Amount = model.Amount;
        transaction.CategoryId = model.CategoryId;
        transaction.LedgerEntityId = model.LedgerEntityId;
        transaction.AccountId = model.AccountId;
        transaction.Notes = model.Notes;
        transaction.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
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
        }

        if (Request.IsHtmxRequest())
            return PartialView("_TransactionRow", null);

        return RedirectToAction(nameof(Index));
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
