using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Services;
using AgriERP.Domain.Entities.Finance;
using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AgriERP.Application.Features.Expenses;

/* ---------------------------- DTOs ---------------------------- */

public class SaveExpenseRequest
{
    public DateTime ExpenseDate { get; set; }
    public int ExpenseCategoryId { get; set; }
    public int PaymentModeId { get; set; }
    public string? PaidTo { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Input credit on a GST expense bill; zero for wages, rent and tea.</summary>
    public decimal GstAmount { get; set; }

    public string? ReferenceNumber { get; set; }
    public string? BillNumber { get; set; }
    public string? Description { get; set; }
}

public class ExpenseDto
{
    public long ExpenseId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
    public int ExpenseCategoryId { get; set; }
    public string ExpenseCategoryName { get; set; } = string.Empty;
    public int PaymentModeId { get; set; }
    public string PaymentModeName { get; set; } = string.Empty;
    public string? PaidTo { get; set; }
    public decimal Amount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? BillNumber { get; set; }
    public string? Description { get; set; }
    public PaymentRecordStatus Status { get; set; }
}

public class ExpenseQueryParameters : QueryParameters
{
    public int? ExpenseCategoryId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class ExpenseCategoryTotalDto
{
    public int ExpenseCategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>Expense totals for a period, used to turn gross profit into net.</summary>
public class ExpenseSummaryDto
{
    public decimal TotalExpenses { get; set; }
    public IReadOnlyList<ExpenseCategoryTotalDto> ByCategory { get; set; } = Array.Empty<ExpenseCategoryTotalDto>();
}

/* ---------------------------- service ---------------------------- */

public interface IExpenseService
{
    Task<PagedResult<ExpenseDto>> GetPagedAsync(ExpenseQueryParameters parameters, CancellationToken ct = default);
    Task<ExpenseDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<ExpenseDto> CreateAsync(SaveExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> UpdateAsync(long id, SaveExpenseRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<ExpenseSummaryDto> GetSummaryAsync(DateTime? fromDate, DateTime? toDate, CancellationToken ct = default);
}

public class ExpenseService : IExpenseService
{
    private readonly IUnitOfWork _uow;
    private readonly IDocumentNumberService _numbers;
    private readonly IGstCalculator _money;

    public ExpenseService(IUnitOfWork uow, IDocumentNumberService numbers, IGstCalculator money)
    {
        _uow = uow;
        _numbers = numbers;
        _money = money;
    }

    /// <summary>
    /// An Expression, not a method - so the category/mode joins translate to SQL
    /// instead of running client-side over un-Included (null) navigations.
    /// </summary>
    private static readonly Expression<Func<Expense, ExpenseDto>> Projection = e => new ExpenseDto
    {
        ExpenseId           = e.ExpenseId,
        VoucherNumber       = e.VoucherNumber,
        ExpenseDate         = e.ExpenseDate,
        ExpenseCategoryId   = e.ExpenseCategoryId,
        ExpenseCategoryName = e.ExpenseCategory!.CategoryName,
        PaymentModeId       = e.PaymentModeId,
        PaymentModeName     = e.PaymentMode!.ModeName,
        PaidTo              = e.PaidTo,
        Amount              = e.Amount,
        GstAmount           = e.GstAmount,
        TotalAmount         = e.TotalAmount,
        ReferenceNumber     = e.ReferenceNumber,
        BillNumber          = e.BillNumber,
        Description         = e.Description,
        Status              = e.Status
    };

    public async Task<PagedResult<ExpenseDto>> GetPagedAsync(
        ExpenseQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Expense>().Query()
            .WhereIf(parameters.ExpenseCategoryId.HasValue,
                e => e.ExpenseCategoryId == parameters.ExpenseCategoryId!.Value)
            .WhereIf(parameters.FromDate.HasValue, e => e.ExpenseDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, e => e.ExpenseDate <= parameters.ToDate!.Value.Date)
            .WhereIf(search is not null, e =>
                e.VoucherNumber.Contains(search!) ||
                (e.PaidTo != null && e.PaidTo.Contains(search!)) ||
                (e.BillNumber != null && e.BillNumber.Contains(search!)));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<ExpenseDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.ExpenseId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(Projection)
            .ToListAsync(ct);

        return PagedResult<ExpenseDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<ExpenseDto> GetByIdAsync(long id, CancellationToken ct = default)
        => await _uow.Repository<Expense>().Query()
            .Where(e => e.ExpenseId == id)
            .Select(Projection)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Expense", id);

    public async Task<ExpenseDto> CreateAsync(SaveExpenseRequest request, CancellationToken ct = default)
    {
        Validate(request);

        var expenseId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var expense = new Expense
            {
                VoucherNumber     = await _numbers.NextAsync(DocumentType.Expense, token),
                ExpenseDate       = request.ExpenseDate.Date,
                ExpenseCategoryId = request.ExpenseCategoryId,
                PaymentModeId     = request.PaymentModeId,
                PaidTo            = request.PaidTo,
                Amount            = _money.Money(request.Amount),
                GstAmount         = _money.Money(request.GstAmount),
                ReferenceNumber   = request.ReferenceNumber,
                BillNumber        = request.BillNumber,
                Description       = request.Description,
                Status            = PaymentRecordStatus.Posted
                // TotalAmount is a DB computed column - never assigned here.
            };

            await _uow.Repository<Expense>().AddAsync(expense, token);
            await _uow.SaveChangesAsync(token);
            return expense.ExpenseId;
        }, ct);

        return await GetByIdAsync(expenseId, ct);
    }

    public async Task<ExpenseDto> UpdateAsync(long id, SaveExpenseRequest request, CancellationToken ct = default)
    {
        Validate(request);

        var expense = await _uow.Repository<Expense>()
            .FirstOrDefaultAsync(e => e.ExpenseId == id, tracking: true, ct)
            ?? throw new NotFoundException("Expense", id);

        // The voucher number is allocated once and kept, like every other document.
        expense.ExpenseDate       = request.ExpenseDate.Date;
        expense.ExpenseCategoryId = request.ExpenseCategoryId;
        expense.PaymentModeId     = request.PaymentModeId;
        expense.PaidTo            = request.PaidTo;
        expense.Amount            = _money.Money(request.Amount);
        expense.GstAmount         = _money.Money(request.GstAmount);
        expense.ReferenceNumber   = request.ReferenceNumber;
        expense.BillNumber        = request.BillNumber;
        expense.Description       = request.Description;

        _uow.Repository<Expense>().Update(expense);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var expense = await _uow.Repository<Expense>()
            .FirstOrDefaultAsync(e => e.ExpenseId == id, tracking: true, ct)
            ?? throw new NotFoundException("Expense", id);

        _uow.Repository<Expense>().Remove(expense);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ExpenseSummaryDto> GetSummaryAsync(
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        var byCategory = await _uow.Repository<Expense>().Query()
            .WhereIf(fromDate.HasValue, e => e.ExpenseDate >= fromDate!.Value.Date)
            .WhereIf(toDate.HasValue, e => e.ExpenseDate <= toDate!.Value.Date)
            .GroupBy(e => new { e.ExpenseCategoryId, e.ExpenseCategory!.CategoryName })
            .Select(g => new ExpenseCategoryTotalDto
            {
                ExpenseCategoryId = g.Key.ExpenseCategoryId,
                CategoryName      = g.Key.CategoryName,
                Amount            = g.Sum(e => e.TotalAmount)
            })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct);

        return new ExpenseSummaryDto
        {
            TotalExpenses = byCategory.Sum(x => x.Amount),
            ByCategory    = byCategory
        };
    }

    private static void Validate(SaveExpenseRequest request)
    {
        if (request.ExpenseCategoryId <= 0)
            throw new ValidationException(nameof(request.ExpenseCategoryId), "Select an expense category.");
        if (request.PaymentModeId <= 0)
            throw new ValidationException(nameof(request.PaymentModeId), "Select a payment mode.");
        if (request.Amount <= 0)
            throw new ValidationException(nameof(request.Amount), "Amount must be greater than zero.");
        if (request.GstAmount < 0)
            throw new ValidationException(nameof(request.GstAmount), "GST cannot be negative.");
    }
}
