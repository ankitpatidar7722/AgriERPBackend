using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Services;
using AgriERP.Application.Features.Sales.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Entities.Sales;
using AgriERP.Domain.Entities.System;
using AgriERP.Domain.Enums;
using AgriERP.Domain.ReadModels;
using AgriERP.Shared.Constants;
using AgriERP.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AgriERP.Application.Features.Sales;

public interface ISalesService
{
    Task<PagedResult<SaleListDto>> GetPagedAsync(SaleQueryParameters parameters, CancellationToken ct = default);
    Task<SaleDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<SaleDto> CreateAsync(SaveSaleRequest request, CancellationToken ct = default);
    Task<SaleDto> PostAsync(long id, CancellationToken ct = default);
    Task<SaleDto> CancelAsync(long id, string reason, CancellationToken ct = default);
    Task<InvoicePrintDto> GetInvoiceForPrintAsync(long id, CancellationToken ct = default);

    Task<PagedResult<SalesReturnDto>> GetReturnsAsync(SalesReturnQueryParameters parameters, CancellationToken ct = default);
    Task<SalesReturnDto> GetReturnAsync(long id, CancellationToken ct = default);
    Task<SalesReturnDto> CreateReturnAsync(SaveSalesReturnRequest request, CancellationToken ct = default);
    Task<SalesReturnDto> PostReturnAsync(long id, CancellationToken ct = default);
}

public class SalesService : ISalesService
{
    private readonly IUnitOfWork _uow;
    private readonly IStockPostingService _posting;
    private readonly IBatchAllocator _allocator;
    private readonly IDocumentNumberService _numbers;
    private readonly IGstCalculator _gst;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;

    public SalesService(
        IUnitOfWork uow,
        IStockPostingService posting,
        IBatchAllocator allocator,
        IDocumentNumberService numbers,
        IGstCalculator gst,
        IDateTimeProvider clock,
        ICurrentUserService currentUser)
    {
        _uow = uow;
        _posting = posting;
        _allocator = allocator;
        _numbers = numbers;
        _gst = gst;
        _clock = clock;
        _currentUser = currentUser;
    }

    /* ============================ queries ============================ */

    public async Task<PagedResult<SaleListDto>> GetPagedAsync(
        SaleQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Sale>().Query()
            .WhereIf(parameters.CustomerId.HasValue, s => s.CustomerId == parameters.CustomerId)
            .WhereIf(parameters.FromDate.HasValue, s => s.InvoiceDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, s => s.InvoiceDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.Status.HasValue, s => s.Status == parameters.Status!.Value)
            .WhereIf(parameters.SaleType.HasValue, s => s.SaleType == parameters.SaleType!.Value)
            .WhereIf(parameters.PaymentType.HasValue, s => s.PaymentType == parameters.PaymentType!.Value)
            .WhereIf(parameters.SalesmanId.HasValue, s => s.SalesmanId == parameters.SalesmanId)
            .WhereIf(parameters.UnpaidOnly == true,
                     s => s.Status == DocumentStatus.Posted && s.BalanceAmount > 0)
            .WhereIf(search is not null, s =>
                s.InvoiceNumber.Contains(search!) ||
                (s.Customer != null && s.Customer.CustomerName.Contains(search!)) ||
                (s.Customer != null && s.Customer.Mobile != null && s.Customer.Mobile.Contains(search!)) ||
                (s.WalkInCustomerName != null && s.WalkInCustomerName.Contains(search!)) ||
                (s.WalkInMobile != null && s.WalkInMobile.Contains(search!)));

        query = parameters.SortBy?.Trim().ToLowerInvariant() switch
        {
            "customer" => query.OrderByDirection(s => s.Customer!.CustomerName, parameters.SortDescending),
            "amount"   => query.OrderByDirection(s => s.GrandTotal, parameters.SortDescending),
            "balance"  => query.OrderByDirection(s => s.BalanceAmount, parameters.SortDescending),
            "number"   => query.OrderByDirection(s => s.InvoiceNumber, parameters.SortDescending),
            _          => query.OrderByDescending(s => s.InvoiceDate).ThenByDescending(s => s.SaleId)
        };

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<SaleListDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(s => new SaleListDto
            {
                SaleId         = s.SaleId,
                InvoiceNumber  = s.InvoiceNumber,
                InvoiceDate    = s.InvoiceDate,
                CustomerId     = s.CustomerId,
                CustomerName   = s.Customer != null
                                    ? s.Customer.CustomerName
                                    : (s.WalkInCustomerName ?? "Cash Customer"),
                Village        = s.Customer != null ? s.Customer.Village : null,
                SaleType       = s.SaleType,
                PaymentType    = s.PaymentType,
                TaxableAmount  = s.TaxableAmount,
                GrandTotal     = s.GrandTotal,
                ReceivedAmount = s.ReceivedAmount,
                BalanceAmount  = s.BalanceAmount,
                PaymentStatus  = s.PaymentStatus.ToString(),
                Status         = s.Status,
                LineCount      = s.Details.Count
            })
            .ToListAsync(ct);

        return PagedResult<SaleListDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<SaleDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<Sale>().Query()
            .Where(s => s.SaleId == id)
            .Select(s => new SaleDto
            {
                SaleId             = s.SaleId,
                InvoiceNumber      = s.InvoiceNumber,
                InvoiceDate        = s.InvoiceDate,
                CustomerId         = s.CustomerId,
                CustomerName       = s.Customer != null
                                        ? s.Customer.CustomerName
                                        : (s.WalkInCustomerName ?? "Cash Customer"),
                Village            = s.Customer != null ? s.Customer.Village : null,
                WalkInCustomerName = s.WalkInCustomerName,
                WalkInMobile       = s.WalkInMobile,
                SaleType           = s.SaleType,
                PaymentType        = s.PaymentType,
                LocationId         = s.LocationId,
                LocationName       = s.Location!.LocationName,
                SalesmanId         = s.SalesmanId,
                SalesmanName       = s.Salesman != null ? s.Salesman.FullName : null,
                IsInterState       = s.IsInterState,
                GrossAmount        = s.GrossAmount,
                DiscountAmount     = s.DiscountAmount,
                TaxableAmount      = s.TaxableAmount,
                CgstAmount         = s.CgstAmount,
                SgstAmount         = s.SgstAmount,
                IgstAmount         = s.IgstAmount,
                OtherCharges       = s.OtherCharges,
                RoundOff           = s.RoundOff,
                GrandTotal         = s.GrandTotal,
                ReceivedAmount     = s.ReceivedAmount,
                BalanceAmount      = s.BalanceAmount,
                PaymentStatus      = s.PaymentStatus.ToString(),
                Status             = s.Status,
                DueDate            = s.DueDate,
                Remarks            = s.Remarks,
                PostedAt           = s.PostedAt,
                CancelledAt        = s.CancelledAt,
                CancelReason       = s.CancelReason,
                PrintCount         = s.PrintCount,
                CreatedAt          = s.CreatedAt,
                TotalCostAmount    = s.TotalCostAmount,
                GrossProfit        = s.GrossProfit,
                LineCount          = s.Details.Count
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Invoice", id);

        // Cost and margin are stripped for anyone without Report.Profit. A
        // salesman can raise and reprint a bill without seeing what the shop
        // makes on it.
        var canSeeProfit = _currentUser.HasPermission(Permissions.Report.Profit);

        if (!canSeeProfit)
        {
            dto.TotalCostAmount = null;
            dto.GrossProfit = null;
        }

        var lines = await _uow.Repository<SalesDetail>().Query()
            .Where(d => d.SaleId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new SaleLineDto
            {
                SalesDetailId   = d.SalesDetailId,
                LineNumber      = d.LineNumber,
                ItemId       = d.ItemId,
                ItemName     = d.Item!.ItemName,
                UnitCode        = d.Unit!.UnitCode,
                BatchId         = d.BatchId,
                BatchNumber     = d.BatchNumber,
                ExpiryDate      = d.ExpiryDate,
                Quantity        = d.Quantity,
                FreeQuantity    = d.FreeQuantity,
                TotalQuantity   = d.TotalQuantity,
                Mrp             = d.Mrp,
                Rate            = d.Rate,
                GrossAmount     = d.GrossAmount,
                DiscountPercent = d.DiscountPercent,
                DiscountAmount  = d.DiscountAmount,
                TaxableAmount   = d.TaxableAmount,
                GstPercent      = d.GstPercent,
                CgstAmount      = d.CgstAmount,
                SgstAmount      = d.SgstAmount,
                IgstAmount      = d.IgstAmount,
                LineTotal       = d.LineTotal,
                HsnCode         = d.HsnCode,
                CostRate        = d.CostRate,
                LineProfit      = d.LineProfit
            })
            .ToListAsync(ct);

        if (!canSeeProfit)
            foreach (var line in lines) { line.CostRate = null; line.LineProfit = null; }

        dto.Lines = lines;

        dto.Payments = await _uow.Repository<SalePayment>().Query()
            .Where(p => p.SaleId == id)
            .Select(p => new SalePaymentDto
            {
                SalePaymentId   = p.SalePaymentId,
                PaymentModeId   = p.PaymentModeId,
                PaymentModeName = p.PaymentMode!.ModeName,
                Amount          = p.Amount,
                ReferenceNumber = p.ReferenceNumber,
                BankName        = p.BankName,
                ChequeDate      = p.ChequeDate
            })
            .ToListAsync(ct);

        return dto;
    }

    /* ============================ create ============================ */

    public async Task<SaleDto> CreateAsync(SaveSaleRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        // CK_Sales_CreditNeedsCustomer enforces this at the database too. It is
        // checked here so the operator gets a sentence rather than a constraint
        // violation.
        if (request.PaymentType == SalePaymentType.Credit && request.CustomerId is null)
            throw new ValidationException(nameof(request.CustomerId),
                "A credit sale needs a named customer. Select one, or bill it as cash.");

        Customer? customer = null;

        if (request.CustomerId is { } customerId)
        {
            customer = await _uow.Repository<Customer>()
                .FirstOrDefaultAsync(c => c.CustomerId == customerId && !c.IsDeleted, tracking: false, ct)
                ?? throw new ValidationException(nameof(request.CustomerId), "The selected customer does not exist.");
        }

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);
        var isInterState = await IsInterStateAsync(customer?.StateId, ct);

        var saleId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var sale = new Sale
            {
                InvoiceNumber        = await _numbers.NextAsync(DocumentType.Sale, token),
                InvoiceDate          = request.InvoiceDate.Date,
                InvoiceTime          = _clock.UtcNow.ToLocalTime().TimeOfDay,
                CustomerId           = request.CustomerId,
                WalkInCustomerName   = Blank(request.WalkInCustomerName),
                WalkInMobile         = Blank(request.WalkInMobile),
                SaleType             = request.SaleType,
                PaymentType          = request.PaymentType,
                LocationId           = locationId,
                SalesmanId           = request.SalesmanId ?? _currentUser.UserId,
                IsInterState         = isInterState,
                PlaceOfSupplyStateId = customer?.StateId,
                OtherCharges         = request.OtherCharges,
                Remarks              = request.Remarks,
                Status               = DocumentStatus.Draft,
                // The bill can carry its own credit term; otherwise the
                // customer's default applies.
                DueDate              = customer is not null
                                          ? request.InvoiceDate.Date.AddDays(request.CreditDays ?? customer.CreditDays)
                                          : null
            };

            await _uow.Repository<Sale>().AddAsync(sale, token);
            await _uow.SaveChangesAsync(token);

            await BuildLinesAsync(sale, request, isInterState, locationId, token);
            await AddPaymentsAsync(sale, request, token);

            return sale.SaleId;
        }, ct);

        await EnforceCreditLimitAsync(saleId, ct);
        return await GetByIdAsync(saleId, ct);
    }

    private async Task BuildLinesAsync(
        Sale sale, SaveSaleRequest request, bool isInterState, int locationId, CancellationToken ct)
    {
        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();

        var items = await _uow.Repository<Item>().Query()
            .Where(p => itemIds.Contains(p.ItemId) && !p.IsDeleted)
            .Select(p => new
            {
                p.ItemId, p.ItemName, p.UnitId, p.SellingRate, p.WholesaleRate,
                p.DealerRate, p.MinSellingRate, p.Mrp,
                GstPercent = p.GstSlab!.TotalRate,
                HsnCode = p.Hsn != null ? p.Hsn.Code : null
            })
            .ToDictionaryAsync(p => p.ItemId, ct);

        var canOverrideMinRate = _currentUser.HasPermission(Permissions.Sales.OverrideMinRate);
        var lineNumber = 0;
        var details = new List<SalesDetail>();

        foreach (var requested in request.Lines)
        {
            if (!items.TryGetValue(requested.ItemId, out var item))
                throw new ValidationException(nameof(requested.ItemId),
                    $"Item {requested.ItemId} does not exist.");

            if (requested.Quantity <= 0)
                throw new ValidationException(nameof(requested.Quantity),
                    $"{item.ItemName}: quantity must be greater than zero.");

            var rate = requested.Rate ?? request.SaleType switch
            {
                SaleType.Wholesale => item.WholesaleRate > 0 ? item.WholesaleRate : item.SellingRate,
                SaleType.Dealer    => item.DealerRate > 0 ? item.DealerRate : item.SellingRate,
                _                  => item.SellingRate
            };

            if (item.MinSellingRate > 0 && rate < item.MinSellingRate && !canOverrideMinRate)
                throw new BusinessRuleException(
                    $"{item.ItemName}: {rate:N2} is below the minimum selling rate of " +
                    $"{item.MinSellingRate:N2}. This needs the override permission.",
                    "BELOW_MIN_RATE");

            // FEFO may split one requested line across several batches, so one
            // request line can become several invoice lines. That is deliberate:
            // each invoice line must name exactly one batch and expiry, both
            // for the printed bill and for honest costing.
            var allocations = requested.BatchId is { } batchId
                ? new[] { await _allocator.AllocateFromBatchAsync(batchId, requested.Quantity + requested.FreeQuantity, ct) }
                : (await _allocator.AllocateAsync(
                        requested.ItemId, locationId, requested.Quantity + requested.FreeQuantity, ct)).ToArray();

            var remainingPaid = requested.Quantity;
            var remainingFree = requested.FreeQuantity;

            foreach (var allocation in allocations)
            {
                // Paid quantity is consumed first, so free goods land on the
                // last batch rather than being spread as fractions.
                var paidQty = Math.Min(remainingPaid, allocation.Quantity);
                var freeQty = Math.Min(remainingFree, allocation.Quantity - paidQty);
                remainingPaid -= paidQty;
                remainingFree -= freeQty;

                if (paidQty <= 0 && freeQty <= 0) continue;

                var gross = _gst.Money(paidQty * rate);

                var discount = requested.DiscountPercent > 0
                    ? _gst.Money(gross * requested.DiscountPercent / 100m)
                    // A flat discount belongs to the whole requested line, so it
                    // is apportioned by this batch's share of the quantity.
                    : _gst.Money(requested.DiscountAmount * (requested.Quantity > 0
                                                              ? paidQty / requested.Quantity
                                                              : 0m));

                if (discount > gross) discount = gross;

                var taxable = gross - discount;
                var tax = _gst.Split(taxable, item.GstPercent, isInterState);

                details.Add(new SalesDetail
                {
                    SaleId          = sale.SaleId,
                    LineNumber      = ++lineNumber,
                    ItemId       = requested.ItemId,
                    BatchId         = allocation.BatchId,
                    BatchNumber     = allocation.BatchNumber,
                    ExpiryDate      = allocation.ExpiryDate,
                    Quantity        = paidQty,
                    FreeQuantity    = freeQty,
                    UnitId          = item.UnitId,
                    Mrp             = allocation.Mrp > 0 ? allocation.Mrp : item.Mrp,
                    Rate            = rate,
                    DiscountPercent = requested.DiscountPercent,
                    DiscountAmount  = discount,
                    GstPercent      = item.GstPercent,
                    CgstAmount      = tax.Cgst,
                    SgstAmount      = tax.Sgst,
                    IgstAmount      = tax.Igst,
                    CessAmount      = tax.Cess,
                    // Frozen at the moment of sale. Deriving profit later from
                    // the item's current purchase rate would restate last
                    // year's profit every time a new consignment arrives.
                    CostRate        = allocation.CostRate,
                    HsnCode         = item.HsnCode,
                    Remarks         = requested.Remarks
                });
            }
        }

        await _uow.Repository<SalesDetail>().AddRangeAsync(details, ct);
        await _uow.SaveChangesAsync(ct);
        await RecalculateHeaderAsync(sale.SaleId, ct);
    }

    private async Task AddPaymentsAsync(Sale sale, SaveSaleRequest request, CancellationToken ct)
    {
        if (request.Payments.Count > 0)
        {
            foreach (var payment in request.Payments)
            {
                if (payment.Amount <= 0)
                    throw new ValidationException(nameof(payment.Amount), "Payment amount must be greater than zero.");

                await _uow.Repository<SalePayment>().AddAsync(new SalePayment
                {
                    SaleId          = sale.SaleId,
                    PaymentModeId   = payment.PaymentModeId,
                    Amount          = payment.Amount,
                    ReferenceNumber = payment.ReferenceNumber,
                    BankName        = payment.BankName,
                    ChequeDate      = payment.ChequeDate
                }, ct);
            }
        }

        var tracked = await _uow.Repository<Sale>()
            .FirstOrDefaultAsync(s => s.SaleId == sale.SaleId, tracking: true, ct);

        if (tracked is null) return;

        var tendered = request.Payments.Sum(p => p.Amount);

        // A cash sale with no tender lines recorded is settled in full - that
        // is what "cash sale" means at a counter. A credit sale banks only what
        // was actually handed over.
        tracked.ReceivedAmount = tendered > 0
            ? tendered
            : request.PaymentType == SalePaymentType.Cash ? tracked.GrandTotal : 0m;

        if (tracked.ReceivedAmount > tracked.GrandTotal)
            throw new ValidationException(nameof(request.Payments),
                $"Payments total {tracked.ReceivedAmount:N2}, which is more than the invoice total " +
                $"of {tracked.GrandTotal:N2}.");

        await _uow.SaveChangesAsync(ct);
    }

    private async Task RecalculateHeaderAsync(long saleId, CancellationToken ct)
    {
        var sale = await _uow.Repository<Sale>()
            .FirstOrDefaultAsync(s => s.SaleId == saleId, tracking: true, ct);

        if (sale is null) return;

        var totals = await _uow.Repository<SalesDetail>().Query()
            .Where(d => d.SaleId == saleId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Gross    = g.Sum(d => d.GrossAmount),
                Discount = g.Sum(d => d.DiscountAmount),
                Taxable  = g.Sum(d => d.TaxableAmount),
                Cgst     = g.Sum(d => d.CgstAmount),
                Sgst     = g.Sum(d => d.SgstAmount),
                Igst     = g.Sum(d => d.IgstAmount),
                Cess     = g.Sum(d => d.CessAmount),
                Cost     = g.Sum(d => d.CostAmount)
            })
            .FirstOrDefaultAsync(ct);

        sale.GrossAmount     = totals?.Gross ?? 0m;
        sale.DiscountAmount  = totals?.Discount ?? 0m;
        sale.TaxableAmount   = totals?.Taxable ?? 0m;
        sale.CgstAmount      = totals?.Cgst ?? 0m;
        sale.SgstAmount      = totals?.Sgst ?? 0m;
        sale.IgstAmount      = totals?.Igst ?? 0m;
        sale.CessAmount      = totals?.Cess ?? 0m;
        sale.TotalCostAmount = totals?.Cost ?? 0m;

        var beforeRounding = sale.TaxableAmount + sale.CgstAmount + sale.SgstAmount
                             + sale.IgstAmount + sale.CessAmount + sale.OtherCharges;

        sale.RoundOff = await GetBoolSettingAsync("Sales.RoundOffInvoice", true, ct)
            ? _gst.RoundOffAdjustment(beforeRounding)
            : 0m;

        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Blocks a credit sale that would push the customer past their limit.
    /// Run after the invoice is built, because the check needs the final total.
    /// </summary>
    private async Task EnforceCreditLimitAsync(long saleId, CancellationToken ct)
    {
        if (!await GetBoolSettingAsync("Sales.EnforceCreditLimit", true, ct)) return;

        var sale = await _uow.Repository<Sale>().Query()
            .Where(s => s.SaleId == saleId)
            .Select(s => new { s.CustomerId, s.PaymentType, s.GrandTotal, s.ReceivedAmount })
            .FirstOrDefaultAsync(ct);

        if (sale?.CustomerId is not { } customerId || sale.PaymentType != SalePaymentType.Credit) return;

        var customer = await _uow.Repository<Customer>().Query()
            .Where(c => c.CustomerId == customerId)
            .Select(c => new { c.CustomerName, c.CreditLimit })
            .FirstAsync(ct);

        if (customer.CreditLimit <= 0) return;   // zero means no limit configured

        var existingDue = await _uow.Repository<CustomerOutstandingView>().Query()
            .Where(v => v.CustomerId == customerId)
            .Select(v => (decimal?)v.OutstandingAmount)
            .FirstOrDefaultAsync(ct) ?? 0m;

        var thisInvoiceCredit = sale.GrandTotal - sale.ReceivedAmount;
        var projected = existingDue + thisInvoiceCredit;

        if (projected > customer.CreditLimit)
            throw new BusinessRuleException(
                $"{customer.CustomerName} would owe {projected:N2}, over the credit limit of " +
                $"{customer.CreditLimit:N2}. Collect payment or raise the limit first.",
                "CREDIT_LIMIT_EXCEEDED");
    }

    /* ============================ post / cancel ============================ */

    public async Task<SaleDto> PostAsync(long id, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var sale = await _uow.Repository<Sale>()
                .FirstOrDefaultAsync(s => s.SaleId == id, tracking: true, token)
                ?? throw new NotFoundException("Invoice", id);

            if (sale.Status != DocumentStatus.Draft)
                throw new BusinessRuleException(
                    $"Invoice {sale.InvoiceNumber} is already {sale.Status}.", "NOT_DRAFT");

            var lines = await _uow.Repository<SalesDetail>().Query()
                .Where(d => d.SaleId == id)
                .OrderBy(d => d.LineNumber)
                .ToListAsync(token);

            if (lines.Count == 0)
                throw new BusinessRuleException("Cannot post an invoice with no lines.", "NO_LINES");

            foreach (var line in lines)
            {
                await _posting.PostAsync(new StockMovement(
                    StockTransactionTypeId.SalesOut,
                    sale.InvoiceDate,
                    line.ItemId,
                    line.BatchId,
                    sale.LocationId,
                    line.Quantity + line.FreeQuantity,
                    line.CostRate,
                    StockReferenceType.Sale,
                    sale.SaleId,
                    line.SalesDetailId,
                    sale.InvoiceNumber), token);
            }

            sale.Status = DocumentStatus.Posted;
            sale.PostedAt = _clock.UtcNow;

            return null;
        }, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<SaleDto> CancelAsync(long id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("reason", "A cancellation reason is required.");

        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var sale = await _uow.Repository<Sale>()
                .FirstOrDefaultAsync(s => s.SaleId == id, tracking: true, token)
                ?? throw new NotFoundException("Invoice", id);

            if (sale.Status == DocumentStatus.Cancelled)
                throw new BusinessRuleException("This invoice is already cancelled.", "ALREADY_CANCELLED");

            var returnCount = await _uow.Repository<SalesReturn>()
                .CountAsync(r => r.SaleId == id && r.Status == DocumentStatus.Posted, token);

            if (returnCount > 0)
                throw new BusinessRuleException(
                    "This invoice has posted sales returns against it. Cancel those first.",
                    "RETURNS_EXIST");

            if (sale.Status == DocumentStatus.Posted)
                await _posting.ReverseDocumentAsync(
                    StockReferenceType.Sale, id,
                    $"Cancellation of {sale.InvoiceNumber}: {reason}", token);

            sale.Status = DocumentStatus.Cancelled;
            sale.CancelledAt = _clock.UtcNow;
            sale.CancelReason = reason.Trim();
            // The money is no longer owed either way, so the invoice stops
            // counting toward the customer's dues.
            sale.ReceivedAmount = 0m;

            return null;
        }, ct);

        return await GetByIdAsync(id, ct);
    }

    /* ============================ print ============================ */

    public async Task<InvoicePrintDto> GetInvoiceForPrintAsync(long id, CancellationToken ct = default)
    {
        var invoice = await GetByIdAsync(id, ct);

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
                InvoiceTerms        = c.InvoiceTerms,
                InvoiceFooterNote   = c.InvoiceFooterNote,
                UpiId               = c.UpiId,
                LogoPath            = c.LogoPath
            })
            .FirstOrDefaultAsync(ct)
            ?? new ShopHeaderDto { ShopName = "My Agriculture Shop" };

        // HSN-wise, rate-wise summary. An Indian tax invoice has to carry it,
        // and it is also the block a buyer checks first.
        var taxSummary = await _uow.Repository<SalesDetail>().Query()
            .Where(d => d.SaleId == id)
            .GroupBy(d => new { d.HsnCode, d.GstPercent })
            .Select(g => new InvoiceTaxSummaryDto
            {
                HsnCode       = g.Key.HsnCode,
                GstPercent    = g.Key.GstPercent,
                TaxableAmount = g.Sum(d => d.TaxableAmount),
                CgstAmount    = g.Sum(d => d.CgstAmount),
                SgstAmount    = g.Sum(d => d.SgstAmount),
                IgstAmount    = g.Sum(d => d.IgstAmount),
                TotalTax      = g.Sum(d => d.CgstAmount + d.SgstAmount + d.IgstAmount + d.CessAmount)
            })
            .OrderBy(s => s.GstPercent)
            .ToListAsync(ct);

        // Print count is bumped here so a reprint is visible. A second copy of
        // a tax invoice is meant to be marked as such.
        var sale = await _uow.Repository<Sale>()
            .FirstOrDefaultAsync(s => s.SaleId == id, tracking: true, ct);

        if (sale is not null)
        {
            sale.PrintCount++;
            await _uow.SaveChangesAsync(ct);
        }

        return new InvoicePrintDto
        {
            Shop          = shop,
            Invoice       = invoice,
            TaxSummary    = taxSummary,
            AmountInWords = AmountToWords.Convert(invoice.GrandTotal)
        };
    }

    /* ============================ returns ============================ */

    public async Task<PagedResult<SalesReturnDto>> GetReturnsAsync(
        SalesReturnQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<SalesReturn>().Query()
            .WhereIf(parameters.CustomerId.HasValue, r => r.CustomerId == parameters.CustomerId)
            .WhereIf(parameters.FromDate.HasValue, r => r.ReturnDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, r => r.ReturnDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.Status.HasValue, r => r.Status == parameters.Status!.Value)
            .WhereIf(search is not null, r => r.ReturnNumber.Contains(search!));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<SalesReturnDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.SalesReturnId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(ReturnProjection)
            .ToListAsync(ct);

        return PagedResult<SalesReturnDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<SalesReturnDto> GetReturnAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<SalesReturn>().Query()
            .Where(r => r.SalesReturnId == id)
            .Select(ReturnProjection)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Sales return", id);

        dto.Lines = await _uow.Repository<SalesReturnDetail>().Query()
            .Where(d => d.SalesReturnId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new SalesReturnLineDto
            {
                SalesReturnDetailId = d.SalesReturnDetailId,
                ItemId     = d.ItemId,
                ItemName   = d.Item!.ItemName,
                BatchId       = d.BatchId,
                BatchNumber   = d.Batch!.BatchNumber,
                Quantity      = d.Quantity,
                Rate          = d.Rate,
                TaxableAmount = d.TaxableAmount,
                GstPercent    = d.GstPercent,
                LineTotal     = d.LineTotal,
                IsSaleable    = d.IsSaleable,
                ReturnReason  = d.ReturnReason
            })
            .ToListAsync(ct);

        return dto;
    }

    private static readonly Expression<Func<SalesReturn, SalesReturnDto>> ReturnProjection = r => new SalesReturnDto
    {
        SalesReturnId    = r.SalesReturnId,
        ReturnNumber     = r.ReturnNumber,
        ReturnDate       = r.ReturnDate,
        CustomerId       = r.CustomerId,
        CustomerName     = r.Customer != null ? r.Customer.CustomerName : "Cash Customer",
        SaleId           = r.SaleId,
        InvoiceNumber    = r.Sale != null ? r.Sale.InvoiceNumber : null,
        CreditNoteNumber = r.CreditNoteNumber,
        ReturnReason     = r.ReturnReason,
        TaxableAmount    = r.TaxableAmount,
        CgstAmount       = r.CgstAmount,
        SgstAmount       = r.SgstAmount,
        IgstAmount       = r.IgstAmount,
        GrandTotal       = r.GrandTotal,
        RefundMode       = r.RefundMode,
        RefundedAmount   = r.RefundedAmount,
        Status           = r.Status,
        PostedAt         = r.PostedAt
    };

    public async Task<SalesReturnDto> CreateReturnAsync(
        SaveSalesReturnRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);

        int? customerStateId = null;
        if (request.CustomerId is { } customerId)
        {
            customerStateId = await _uow.Repository<Customer>().Query()
                .Where(c => c.CustomerId == customerId)
                .Select(c => c.StateId)
                .FirstOrDefaultAsync(ct);
        }

        var isInterState = await IsInterStateAsync(customerStateId, ct);

        var returnId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var salesReturn = new SalesReturn
            {
                ReturnNumber = await _numbers.NextAsync(DocumentType.SalesReturn, token),
                ReturnDate   = request.ReturnDate.Date,
                CustomerId   = request.CustomerId,
                SaleId       = request.SaleId,
                LocationId   = locationId,
                ReturnReason = request.ReturnReason,
                IsInterState = isInterState,
                RefundMode   = request.RefundMode,
                RefundedAmount = request.RefundedAmount,
                Remarks      = request.Remarks,
                Status       = DocumentStatus.Draft
            };

            await _uow.Repository<SalesReturn>().AddAsync(salesReturn, token);
            await _uow.SaveChangesAsync(token);

            var lineNumber = 0;
            var details = new List<SalesReturnDetail>();

            foreach (var line in request.Lines)
            {
                var batch = await _uow.Repository<ItemBatch>().Query()
                    .Where(b => b.BatchId == line.BatchId)
                    .Select(b => new
                    {
                        b.BatchId, b.ItemId, b.PurchaseRate,
                        ItemName = b.Item!.ItemName,
                        UnitId = b.Item.UnitId,
                        GstPercent = b.Item.GstSlab!.TotalRate
                    })
                    .FirstOrDefaultAsync(token)
                    ?? throw new NotFoundException("Batch", line.BatchId);

                if (line.Quantity <= 0)
                    throw new ValidationException(nameof(line.Quantity),
                        $"{batch.ItemName}: return quantity must be greater than zero.");

                var gross = _gst.Money(line.Quantity * line.Rate);
                var taxable = gross - _gst.Money(line.DiscountAmount);
                var tax = _gst.Split(taxable, batch.GstPercent, isInterState);

                // Cost comes back at the batch's own rate, so a sale followed by
                // its return nets to zero profit rather than a phantom gain.
                var costRate = line.SalesDetailId is { } salesDetailId
                    ? await _uow.Repository<SalesDetail>().Query()
                          .Where(d => d.SalesDetailId == salesDetailId)
                          .Select(d => d.CostRate)
                          .FirstOrDefaultAsync(token)
                    : batch.PurchaseRate;

                details.Add(new SalesReturnDetail
                {
                    SalesReturnId  = salesReturn.SalesReturnId,
                    LineNumber     = ++lineNumber,
                    ItemId      = batch.ItemId,
                    BatchId        = batch.BatchId,
                    SalesDetailId  = line.SalesDetailId,
                    Quantity       = line.Quantity,
                    UnitId         = batch.UnitId,
                    Rate           = line.Rate,
                    DiscountAmount = _gst.Money(line.DiscountAmount),
                    GstPercent     = batch.GstPercent,
                    CgstAmount     = tax.Cgst,
                    SgstAmount     = tax.Sgst,
                    IgstAmount     = tax.Igst,
                    CessAmount     = tax.Cess,
                    CostRate       = costRate,
                    IsSaleable     = line.IsSaleable,
                    ReturnReason   = line.ReturnReason
                });
            }

            await _uow.Repository<SalesReturnDetail>().AddRangeAsync(details, token);
            await _uow.SaveChangesAsync(token);

            salesReturn.GrossAmount     = details.Sum(d => d.GrossAmount);
            salesReturn.DiscountAmount  = details.Sum(d => d.DiscountAmount);
            salesReturn.TaxableAmount   = details.Sum(d => d.TaxableAmount);
            salesReturn.CgstAmount      = details.Sum(d => d.CgstAmount);
            salesReturn.SgstAmount      = details.Sum(d => d.SgstAmount);
            salesReturn.IgstAmount      = details.Sum(d => d.IgstAmount);
            salesReturn.CessAmount      = details.Sum(d => d.CessAmount);
            salesReturn.TotalCostAmount = details.Sum(d => d.CostAmount);

            return salesReturn.SalesReturnId;
        }, ct);

        return await GetReturnAsync(returnId, ct);
    }

    public async Task<SalesReturnDto> PostReturnAsync(long id, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var salesReturn = await _uow.Repository<SalesReturn>()
                .FirstOrDefaultAsync(r => r.SalesReturnId == id, tracking: true, token)
                ?? throw new NotFoundException("Sales return", id);

            if (salesReturn.Status != DocumentStatus.Draft)
                throw new BusinessRuleException(
                    $"Return {salesReturn.ReturnNumber} is already {salesReturn.Status}.", "NOT_DRAFT");

            var lines = await _uow.Repository<SalesReturnDetail>().Query()
                .Where(d => d.SalesReturnId == id)
                .ToListAsync(token);

            foreach (var line in lines)
            {
                // Unsaleable goods are recorded on the credit note - the
                // customer is refunded - but never put back into stock. Damaged
                // or expired item must not become sellable again by being
                // handed over the counter.
                if (!line.IsSaleable) continue;

                await _posting.PostAsync(new StockMovement(
                    StockTransactionTypeId.SalesReturnIn,
                    salesReturn.ReturnDate,
                    line.ItemId,
                    line.BatchId,
                    salesReturn.LocationId,
                    line.Quantity,
                    line.CostRate,
                    StockReferenceType.SalesReturn,
                    salesReturn.SalesReturnId,
                    line.SalesReturnDetailId,
                    salesReturn.ReturnNumber,
                    line.ReturnReason), token);
            }

            salesReturn.Status = DocumentStatus.Posted;
            salesReturn.PostedAt = _clock.UtcNow;

            return null;
        }, ct);

        return await GetReturnAsync(id, ct);
    }

    /* ============================ helpers ============================ */

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<bool> IsInterStateAsync(int? partyStateId, CancellationToken ct)
    {
        var shopStateId = await _uow.Repository<CompanyProfile>().Query()
            .Select(c => c.StateId)
            .FirstOrDefaultAsync(ct);

        if (shopStateId is null || partyStateId is null) return false;

        return shopStateId != partyStateId;
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool fallback, CancellationToken ct)
    {
        var value = await _uow.Repository<AppSetting>().Query()
            .Where(s => s.SettingKey == key)
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync(ct);

        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private async Task<int> GetDefaultLocationIdAsync(CancellationToken ct)
    {
        var id = await _uow.Repository<StorageLocation>().Query()
            .Where(l => l.IsDefault && !l.IsDeleted)
            .Select(l => l.LocationId)
            .FirstOrDefaultAsync(ct);

        return id != 0
            ? id
            : throw new BusinessRuleException(
                "No default storage location is configured. Set one in Storage Locations first.",
                "NO_DEFAULT_LOCATION");
    }
}
