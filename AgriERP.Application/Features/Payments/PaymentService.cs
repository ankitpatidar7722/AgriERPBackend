using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Services;
using AgriERP.Application.Features.Sales.Dtos;
using AgriERP.Domain.Entities.Finance;
using AgriERP.Domain.Entities.Purchases;
using AgriERP.Domain.Entities.Sales;
using AgriERP.Domain.Entities.System;
using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AgriERP.Application.Features.Payments;

/* ---------------------------- DTOs ---------------------------- */

public class PaymentAllocationRequest
{
    public AllocationReferenceType ReferenceType { get; set; }
    public long ReferenceId { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public class SavePaymentRequest
{
    public DateTime PaymentDate { get; set; }
    public PartyType PartyType { get; set; }
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public PaymentDirection PaymentType { get; set; }
    public int PaymentModeId { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }
    public ChequeClearanceStatus ClearanceStatus { get; set; } = ChequeClearanceStatus.Cleared;
    public string? Remarks { get; set; }

    /// <summary>
    /// Leave empty to hold the money on account. Anything not allocated stays
    /// as an advance rather than being spread across bills automatically.
    /// </summary>
    public List<PaymentAllocationRequest> Allocations { get; set; } = new();
}

public class PaymentAllocationDto
{
    public long PaymentAllocationId { get; set; }
    public AllocationReferenceType ReferenceType { get; set; }
    public long ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public class PaymentDto
{
    public long PaymentId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public PartyType PartyType { get; set; }
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public PaymentDirection PaymentType { get; set; }
    public int PaymentModeId { get; set; }
    public string PaymentModeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal UnallocatedAmount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }
    public ChequeClearanceStatus ClearanceStatus { get; set; }
    public PaymentRecordStatus Status { get; set; }
    public string? Remarks { get; set; }
    public IReadOnlyList<PaymentAllocationDto> Allocations { get; set; } = Array.Empty<PaymentAllocationDto>();
}

public class PaymentQueryParameters : QueryParameters
{
    public PartyType? PartyType { get; set; }
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public PaymentDirection? PaymentType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public ChequeClearanceStatus? ClearanceStatus { get; set; }
}

/// <summary>An open bill a payment can be applied to.</summary>
public class OpenBillDto
{
    public AllocationReferenceType ReferenceType { get; set; }
    public long ReferenceId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public int AgeDays { get; set; }
}

/* ---------------------------- service ---------------------------- */

public interface IPaymentService
{
    Task<PagedResult<PaymentDto>> GetPagedAsync(PaymentQueryParameters parameters, CancellationToken ct = default);
    Task<PaymentDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<ReceiptPrintDto> GetReceiptForPrintAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<OpenBillDto>> GetOpenBillsAsync(PartyType partyType, int partyId, CancellationToken ct = default);
    Task<PaymentDto> CreateAsync(SavePaymentRequest request, CancellationToken ct = default);
    Task<PaymentDto> CancelAsync(long id, string reason, CancellationToken ct = default);
}

/// <summary>Shop letterhead + the payment + amount in words, for a printable receipt.</summary>
public class ReceiptPrintDto
{
    public ShopHeaderDto Shop { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();
    public string AmountInWords { get; set; } = string.Empty;
}

public class PaymentService : IPaymentService
{
    private readonly IUnitOfWork _uow;
    private readonly IDocumentNumberService _numbers;
    private readonly IGstCalculator _money;
    private readonly IDateTimeProvider _clock;

    public PaymentService(
        IUnitOfWork uow, IDocumentNumberService numbers, IGstCalculator money, IDateTimeProvider clock)
    {
        _uow = uow;
        _numbers = numbers;
        _money = money;
        _clock = clock;
    }

    public async Task<PagedResult<PaymentDto>> GetPagedAsync(
        PaymentQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Payment>().Query()
            .WhereIf(parameters.PartyType.HasValue, p => p.PartyType == parameters.PartyType!.Value)
            .WhereIf(parameters.CustomerId.HasValue, p => p.CustomerId == parameters.CustomerId)
            .WhereIf(parameters.SupplierId.HasValue, p => p.SupplierId == parameters.SupplierId)
            .WhereIf(parameters.PaymentType.HasValue, p => p.PaymentType == parameters.PaymentType!.Value)
            .WhereIf(parameters.FromDate.HasValue, p => p.PaymentDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, p => p.PaymentDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.ClearanceStatus.HasValue, p => p.ClearanceStatus == parameters.ClearanceStatus!.Value)
            .WhereIf(search is not null, p =>
                p.VoucherNumber.Contains(search!) ||
                (p.ReferenceNumber != null && p.ReferenceNumber.Contains(search!)));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<PaymentDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.PaymentId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(PaymentProjection)
            .ToListAsync(ct);

        return PagedResult<PaymentDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<ReceiptPrintDto> GetReceiptForPrintAsync(long id, CancellationToken ct = default)
    {
        var payment = await GetByIdAsync(id, ct);

        var shop = await _uow.Repository<CompanyProfile>().Query()
            .Select(c => new ShopHeaderDto
            {
                ShopName            = c.ShopName,
                GstNumber           = c.GstNumber,
                Address             = c.AddressLine1,
                City                = c.City,
                StateName           = c.State != null ? c.State.StateName : null,
                Pincode             = c.Pincode,
                Phone               = c.Phone,
                Email               = c.Email,
                PesticideLicenceNo  = c.PesticideLicenceNo,
                SeedLicenceNo       = c.SeedLicenceNo,
                FertilizerLicenceNo = c.FertilizerLicenceNo,
                InvoiceFooterNote   = c.InvoiceFooterNote,
                UpiId               = c.UpiId,
                LogoPath            = c.LogoPath
            })
            .FirstOrDefaultAsync(ct)
            ?? new ShopHeaderDto { ShopName = "My Agriculture Shop" };

        return new ReceiptPrintDto
        {
            Shop = shop,
            Payment = payment,
            AmountInWords = AmountToWords.Convert(payment.Amount)
        };
    }

    public async Task<PaymentDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<Payment>().Query()
            .Where(p => p.PaymentId == id)
            .Select(PaymentProjection)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Payment", id);

        dto.Allocations = await _uow.Repository<PaymentAllocation>().Query()
            .Where(a => a.PaymentId == id)
            .Select(a => new PaymentAllocationDto
            {
                PaymentAllocationId = a.PaymentAllocationId,
                ReferenceType       = a.ReferenceType,
                ReferenceId         = a.ReferenceId,
                ReferenceNumber     = a.ReferenceNumber,
                AllocatedAmount     = a.AllocatedAmount
            })
            .ToListAsync(ct);

        return dto;
    }

    /// <summary>
    /// An Expression, not a method.
    ///
    /// A static method called inside Select() cannot be translated, so EF
    /// materialises the entity and runs the method on the client - where every
    /// navigation is null because nothing Included them. That surfaces as a
    /// NullReferenceException at runtime rather than a compile error. As an
    /// Expression it becomes part of the SQL SELECT and the joins come for free.
    /// </summary>
    private static readonly Expression<Func<Payment, PaymentDto>> PaymentProjection = p => new PaymentDto
    {
        PaymentId         = p.PaymentId,
        VoucherNumber     = p.VoucherNumber,
        PaymentDate       = p.PaymentDate,
        PartyType         = p.PartyType,
        CustomerId        = p.CustomerId,
        SupplierId        = p.SupplierId,
        PartyName         = p.Customer != null ? p.Customer.CustomerName
                            : p.Supplier != null ? p.Supplier.SupplierName : string.Empty,
        PaymentType       = p.PaymentType,
        PaymentModeId     = p.PaymentModeId,
        PaymentModeName   = p.PaymentMode!.ModeName,
        Amount            = p.Amount,
        AllocatedAmount   = p.AllocatedAmount,
        UnallocatedAmount = p.UnallocatedAmount,
        ReferenceNumber   = p.ReferenceNumber,
        BankName          = p.BankName,
        ChequeDate        = p.ChequeDate,
        ClearanceStatus   = p.ClearanceStatus,
        Status            = p.Status,
        Remarks           = p.Remarks
    };

    /// <summary>
    /// Open bills for a party, oldest first - the order a collection screen
    /// should offer them, since the oldest debt is the one at most risk.
    /// </summary>
    public async Task<IReadOnlyList<OpenBillDto>> GetOpenBillsAsync(
        PartyType partyType, int partyId, CancellationToken ct = default)
    {
        var today = _clock.Today;

        // AgeDays is filled after materialisation. EF.Functions.DateDiffDay
        // lives in the SQL Server provider, and the Application layer
        // deliberately does not reference a specific provider - depending on
        // one here would make the whole layer untestable against anything else.
        List<OpenBillDto> bills;

        if (partyType == PartyType.Customer)
        {
            bills = await _uow.Repository<Sale>().Query()
                .Where(s => s.CustomerId == partyId
                            && s.Status == DocumentStatus.Posted
                            && s.BalanceAmount > 0)
                .OrderBy(s => s.InvoiceDate)
                .Select(s => new OpenBillDto
                {
                    ReferenceType  = AllocationReferenceType.Sale,
                    ReferenceId    = s.SaleId,
                    DocumentNumber = s.InvoiceNumber,
                    DocumentDate   = s.InvoiceDate,
                    DueDate        = s.DueDate,
                    GrandTotal     = s.GrandTotal,
                    PaidAmount     = s.ReceivedAmount,
                    BalanceAmount  = s.BalanceAmount
                })
                .ToListAsync(ct);
        }
        else
        {
            bills = await _uow.Repository<Purchase>().Query()
                .Where(p => p.SupplierId == partyId
                            && p.Status == DocumentStatus.Posted
                            && p.BalanceAmount > 0)
                .OrderBy(p => p.PurchaseDate)
                .Select(p => new OpenBillDto
                {
                    ReferenceType  = AllocationReferenceType.Purchase,
                    ReferenceId    = p.PurchaseId,
                    DocumentNumber = p.PurchaseNumber,
                    DocumentDate   = p.PurchaseDate,
                    DueDate        = p.DueDate,
                    GrandTotal     = p.GrandTotal,
                    PaidAmount     = p.PaidAmount,
                    BalanceAmount  = p.BalanceAmount
                })
                .ToListAsync(ct);
        }

        foreach (var bill in bills)
            bill.AgeDays = (int)(today - bill.DocumentDate.Date).TotalDays;

        return bills;
    }

    public async Task<PaymentDto> CreateAsync(SavePaymentRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
            throw new ValidationException(nameof(request.Amount), "Amount must be greater than zero.");

        // Mirrors CK_Payments_Party. Checked here so the operator sees a
        // sentence rather than a constraint violation.
        if (request.PartyType == PartyType.Customer && request.CustomerId is null)
            throw new ValidationException(nameof(request.CustomerId), "Select a customer.");

        if (request.PartyType == PartyType.Supplier && request.SupplierId is null)
            throw new ValidationException(nameof(request.SupplierId), "Select a supplier.");

        var allocationTotal = request.Allocations.Sum(a => a.AllocatedAmount);

        if (allocationTotal > request.Amount)
            throw new ValidationException(nameof(request.Allocations),
                $"Allocations total {allocationTotal:N2}, more than the {request.Amount:N2} received.");

        var paymentId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var documentType = request.PaymentType == PaymentDirection.Receipt
                ? DocumentType.Receipt
                : DocumentType.Payment;

            var payment = new Payment
            {
                VoucherNumber   = await _numbers.NextAsync(documentType, token),
                PaymentDate     = request.PaymentDate.Date,
                PartyType       = request.PartyType,
                CustomerId      = request.PartyType == PartyType.Customer ? request.CustomerId : null,
                SupplierId      = request.PartyType == PartyType.Supplier ? request.SupplierId : null,
                PaymentType     = request.PaymentType,
                PaymentModeId   = request.PaymentModeId,
                Amount          = _money.Money(request.Amount),
                ReferenceNumber = request.ReferenceNumber,
                BankName        = request.BankName,
                ChequeDate      = request.ChequeDate,
                ClearanceStatus = request.ClearanceStatus,
                ClearedDate     = request.ClearanceStatus == ChequeClearanceStatus.Cleared
                                      ? request.PaymentDate.Date : null,
                Remarks         = request.Remarks,
                Status          = PaymentRecordStatus.Posted
            };

            await _uow.Repository<Payment>().AddAsync(payment, token);
            await _uow.SaveChangesAsync(token);

            decimal allocated = 0m;

            foreach (var allocation in request.Allocations)
            {
                if (allocation.AllocatedAmount <= 0)
                    throw new ValidationException(nameof(allocation.AllocatedAmount),
                        "Allocated amount must be greater than zero.");

                var reference = await ApplyAllocationAsync(allocation, token);

                await _uow.Repository<PaymentAllocation>().AddAsync(new PaymentAllocation
                {
                    PaymentId       = payment.PaymentId,
                    ReferenceType   = allocation.ReferenceType,
                    ReferenceId     = allocation.ReferenceId,
                    ReferenceNumber = reference,
                    AllocatedAmount = allocation.AllocatedAmount
                }, token);

                allocated += allocation.AllocatedAmount;
            }

            payment.AllocatedAmount = allocated;
            return payment.PaymentId;
        }, ct);

        return await GetByIdAsync(paymentId, ct);
    }

    /// <summary>
    /// Applies one allocation to its bill and returns the document number.
    ///
    /// The bill's paid/received column is the authoritative record of what has
    /// been settled against it - the outstanding views read it directly - so it
    /// has to move in the same transaction as the allocation row.
    /// </summary>
    private async Task<string> ApplyAllocationAsync(PaymentAllocationRequest allocation, CancellationToken ct)
    {
        switch (allocation.ReferenceType)
        {
            case AllocationReferenceType.Sale:
            {
                var sale = await _uow.Repository<Sale>()
                    .FirstOrDefaultAsync(s => s.SaleId == allocation.ReferenceId, tracking: true, ct)
                    ?? throw new NotFoundException("Invoice", allocation.ReferenceId);

                if (sale.Status != DocumentStatus.Posted)
                    throw new BusinessRuleException(
                        $"Invoice {sale.InvoiceNumber} is {sale.Status} and cannot take a payment.",
                        "NOT_POSTED");

                if (allocation.AllocatedAmount > sale.BalanceAmount)
                    throw new BusinessRuleException(
                        $"Invoice {sale.InvoiceNumber} has {sale.BalanceAmount:N2} outstanding, " +
                        $"but {allocation.AllocatedAmount:N2} was allocated to it.",
                        "OVER_ALLOCATION");

                sale.ReceivedAmount += allocation.AllocatedAmount;
                return sale.InvoiceNumber;
            }

            case AllocationReferenceType.Purchase:
            {
                var purchase = await _uow.Repository<Purchase>()
                    .FirstOrDefaultAsync(p => p.PurchaseId == allocation.ReferenceId, tracking: true, ct)
                    ?? throw new NotFoundException("Purchase", allocation.ReferenceId);

                if (purchase.Status != DocumentStatus.Posted)
                    throw new BusinessRuleException(
                        $"Purchase {purchase.PurchaseNumber} is {purchase.Status} and cannot take a payment.",
                        "NOT_POSTED");

                if (allocation.AllocatedAmount > purchase.BalanceAmount)
                    throw new BusinessRuleException(
                        $"Purchase {purchase.PurchaseNumber} has {purchase.BalanceAmount:N2} outstanding, " +
                        $"but {allocation.AllocatedAmount:N2} was allocated to it.",
                        "OVER_ALLOCATION");

                purchase.PaidAmount += allocation.AllocatedAmount;
                return purchase.PurchaseNumber;
            }

            default:
                throw new BusinessRuleException(
                    $"Allocating against {allocation.ReferenceType} is not supported yet.",
                    "UNSUPPORTED_ALLOCATION");
        }
    }

    public async Task<PaymentDto> CancelAsync(long id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("reason", "A cancellation reason is required.");

        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var payment = await _uow.Repository<Payment>()
                .FirstOrDefaultAsync(p => p.PaymentId == id, tracking: true, token)
                ?? throw new NotFoundException("Payment", id);

            if (payment.Status == PaymentRecordStatus.Cancelled)
                throw new BusinessRuleException("This payment is already cancelled.", "ALREADY_CANCELLED");

            var allocations = await _uow.Repository<PaymentAllocation>().Query()
                .Where(a => a.PaymentId == id)
                .ToListAsync(token);

            // Every bill this payment touched has to be reopened, or a bounced
            // cheque would leave invoices looking settled while the money never
            // arrived.
            foreach (var allocation in allocations)
            {
                switch (allocation.ReferenceType)
                {
                    case AllocationReferenceType.Sale:
                        var sale = await _uow.Repository<Sale>()
                            .FirstOrDefaultAsync(s => s.SaleId == allocation.ReferenceId, tracking: true, token);
                        if (sale is not null) sale.ReceivedAmount -= allocation.AllocatedAmount;
                        break;

                    case AllocationReferenceType.Purchase:
                        var purchase = await _uow.Repository<Purchase>()
                            .FirstOrDefaultAsync(p => p.PurchaseId == allocation.ReferenceId, tracking: true, token);
                        if (purchase is not null) purchase.PaidAmount -= allocation.AllocatedAmount;
                        break;
                }
            }

            payment.Status = PaymentRecordStatus.Cancelled;
            payment.CancelledAt = _clock.UtcNow;
            payment.CancelReason = reason.Trim();
            payment.AllocatedAmount = 0m;

            return null;
        }, ct);

        return await GetByIdAsync(id, ct);
    }
}
