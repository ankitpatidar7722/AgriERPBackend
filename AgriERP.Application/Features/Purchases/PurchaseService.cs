using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Services;
using AgriERP.Application.Features.Purchases.Dtos;
using AgriERP.Application.Features.Sales.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Entities.Purchases;
using AgriERP.Domain.Entities.System;
using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AgriERP.Application.Features.Purchases;

public interface IPurchaseService
{
    Task<PagedResult<PurchaseListDto>> GetPagedAsync(PurchaseQueryParameters parameters, CancellationToken ct = default);
    Task<PagedResult<PurchaseItemRowDto>> GetPurchaseItemsAsync(PurchaseQueryParameters parameters, CancellationToken ct = default);
    Task<PurchaseDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PurchasePrintDto> GetPurchaseForPrintAsync(long id, CancellationToken ct = default);
    Task<PurchaseDto> CreateAsync(SavePurchaseRequest request, CancellationToken ct = default);
    Task<PurchaseDto> UpdateAsync(long id, SavePurchaseRequest request, CancellationToken ct = default);
    Task<PurchaseDto> PostAsync(long id, CancellationToken ct = default);
    Task<PurchaseDto> CancelAsync(long id, string reason, CancellationToken ct = default);

    Task<PagedResult<PurchaseReturnDto>> GetReturnsAsync(PurchaseReturnQueryParameters parameters, CancellationToken ct = default);
    Task<PurchaseReturnDto> GetReturnAsync(long id, CancellationToken ct = default);
    Task<PurchaseReturnDto> CreateReturnAsync(SavePurchaseReturnRequest request, CancellationToken ct = default);
    Task<PurchaseReturnDto> PostReturnAsync(long id, CancellationToken ct = default);

    Task<PagedResult<PurchaseOrderDto>> GetOrdersAsync(PurchaseOrderQueryParameters parameters, CancellationToken ct = default);
    Task<PagedResult<PurchaseOrderItemRowDto>> GetOrderItemsAsync(PurchaseOrderQueryParameters parameters, CancellationToken ct = default);
    Task<PurchaseOrderDto> GetOrderAsync(long id, CancellationToken ct = default);
    Task<PurchaseOrderPrintDto> GetOrderForPrintAsync(long id, CancellationToken ct = default);
    Task<PurchaseOrderDto> CreateOrderAsync(SavePurchaseOrderRequest request, CancellationToken ct = default);
    Task<PurchaseOrderDto> UpdateOrderAsync(long id, SavePurchaseOrderRequest request, CancellationToken ct = default);

    Task<PagedResult<PurchaseRequisitionDto>> GetRequisitionsAsync(PurchaseRequisitionQueryParameters parameters, CancellationToken ct = default);
    Task<PagedResult<PurchaseRequisitionItemRowDto>> GetRequisitionItemsAsync(PurchaseRequisitionQueryParameters parameters, CancellationToken ct = default);
    Task<string?> PeekNextRequisitionNumberAsync(CancellationToken ct = default);
    Task<string?> PeekNextOrderNumberAsync(CancellationToken ct = default);
    Task<PurchaseRequisitionDto> GetRequisitionAsync(long id, CancellationToken ct = default);
    Task<PurchaseRequisitionDto> CreateRequisitionAsync(SavePurchaseRequisitionRequest request, CancellationToken ct = default);
    Task<PurchaseRequisitionDto> UpdateRequisitionAsync(long id, SavePurchaseRequisitionRequest request, CancellationToken ct = default);
    Task<PurchaseRequisitionDto> CancelRequisitionAsync(long id, CancellationToken ct = default);
}

public class PurchaseService : IPurchaseService
{
    private readonly IUnitOfWork _uow;
    private readonly IStockPostingService _posting;
    private readonly IDocumentNumberService _numbers;
    private readonly IGstCalculator _gst;
    private readonly IDateTimeProvider _clock;

    public PurchaseService(
        IUnitOfWork uow,
        IStockPostingService posting,
        IDocumentNumberService numbers,
        IGstCalculator gst,
        IDateTimeProvider clock)
    {
        _uow = uow;
        _posting = posting;
        _numbers = numbers;
        _gst = gst;
        _clock = clock;
    }

    // The Indus-style voucher registry. New documents are tagged so the same
    // purchase tables can carry both an order (booking) and a goods-receipt
    // (stock in), told apart by voucher. The ids are stable once seeded, so a
    // process-wide cache spares a lookup on every save.
    private const string VoucherPurchaseRequisition = "PREQ";
    private const string VoucherPurchaseOrder = "PO";
    private const string VoucherPurchaseGrn   = "PGRN";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _voucherIds = new();

    private async Task<int?> ResolveVoucherIdAsync(string code, CancellationToken ct)
    {
        if (_voucherIds.TryGetValue(code, out var cached)) return cached;
        var voucher = await _uow.Repository<VoucherMaster>()
            .FirstOrDefaultAsync(v => v.VoucherCode == code, tracking: false, ct);
        if (voucher is null) return null;
        _voucherIds[code] = voucher.VoucherId;
        return voucher.VoucherId;
    }

    /* ============================ queries ============================ */

    public async Task<PagedResult<PurchaseListDto>> GetPagedAsync(
        PurchaseQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Purchase>().Query()
            .WhereIf(parameters.SupplierId.HasValue, p => p.SupplierId == parameters.SupplierId)
            .WhereIf(parameters.FromDate.HasValue, p => p.PurchaseDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, p => p.PurchaseDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.Status.HasValue, p => p.Status == parameters.Status!.Value)
            .WhereIf(parameters.UnpaidOnly == true,
                     p => p.Status == DocumentStatus.Posted && p.BalanceAmount > 0)
            .WhereIf(search is not null, p =>
                p.PurchaseNumber.Contains(search!) ||
                (p.SupplierInvoiceNumber != null && p.SupplierInvoiceNumber.Contains(search!)) ||
                p.Supplier!.SupplierName.Contains(search!));

        query = parameters.SortBy?.Trim().ToLowerInvariant() switch
        {
            "supplier" => query.OrderByDirection(p => p.Supplier!.SupplierName, parameters.SortDescending),
            "amount"   => query.OrderByDirection(p => p.GrandTotal, parameters.SortDescending),
            "balance"  => query.OrderByDirection(p => p.BalanceAmount, parameters.SortDescending),
            "number"   => query.OrderByDirection(p => p.PurchaseNumber, parameters.SortDescending),
            _          => query.OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.PurchaseId)
        };

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<PurchaseListDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(p => new PurchaseListDto
            {
                PurchaseId            = p.PurchaseId,
                PurchaseNumber        = p.PurchaseNumber,
                PurchaseDate          = p.PurchaseDate,
                SupplierId            = p.SupplierId,
                SupplierName          = p.Supplier!.SupplierName,
                SupplierInvoiceNumber = p.SupplierInvoiceNumber,
                TaxableAmount         = p.TaxableAmount,
                GrandTotal            = p.GrandTotal,
                PaidAmount            = p.PaidAmount,
                BalanceAmount         = p.BalanceAmount,
                PaymentStatus         = p.PaymentStatus.ToString(),
                Status                = p.Status,
                DueDate               = p.DueDate,
                LineCount             = p.Details.Count,
                WarehouseName         = p.Warehouse != null ? p.Warehouse.WarehouseName : null
            })
            .ToListAsync(ct);

        return PagedResult<PurchaseListDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    /// <summary>
    /// The GRN list flattened to one row PER LINE ITEM (item-wise view): a 5-item
    /// receipt returns 5 rows, each carrying its parent GRN's number, date,
    /// supplier, warehouse and status. Same filters/paging as GetPagedAsync.
    /// </summary>
    public async Task<PagedResult<PurchaseItemRowDto>> GetPurchaseItemsAsync(
        PurchaseQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<PurchaseDetail>().Query()
            .WhereIf(parameters.SupplierId.HasValue, d => d.Purchase!.SupplierId == parameters.SupplierId)
            .WhereIf(parameters.FromDate.HasValue, d => d.Purchase!.PurchaseDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, d => d.Purchase!.PurchaseDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.Status.HasValue, d => d.Purchase!.Status == parameters.Status!.Value)
            .WhereIf(parameters.UnpaidOnly == true,
                     d => d.Purchase!.Status == DocumentStatus.Posted && d.Purchase!.BalanceAmount > 0)
            .WhereIf(search is not null, d =>
                d.Purchase!.PurchaseNumber.Contains(search!)
                || (d.Purchase!.SupplierInvoiceNumber != null && d.Purchase!.SupplierInvoiceNumber.Contains(search!))
                || d.Purchase!.Supplier!.SupplierName.Contains(search!)
                || d.Item!.ItemName.Contains(search!)
                || d.Item!.ItemCode.Contains(search!));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<PurchaseItemRowDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(d => d.Purchase!.PurchaseDate)
            .ThenByDescending(d => d.PurchaseId)
            .ThenBy(d => d.LineNumber)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(d => new PurchaseItemRowDto
            {
                PurchaseId       = d.PurchaseId,
                PurchaseNumber   = d.Purchase!.PurchaseNumber,
                PurchaseDate     = d.Purchase.PurchaseDate,
                SupplierId       = d.Purchase.SupplierId,
                SupplierName     = d.Purchase.Supplier!.SupplierName,
                WarehouseName    = d.Purchase.Warehouse != null ? d.Purchase.Warehouse.WarehouseName : null,
                Status           = d.Purchase.Status,
                PurchaseDetailId = d.PurchaseDetailId,
                ItemCode         = d.Item!.ItemCode,
                ItemName         = d.Item.ItemName,
                ItemGroupName    = d.Item.ItemGroup!.ItemGroupName,
                ItemSubGroupName = d.Item.ItemSubGroup!.ItemSubGroupName,
                UnitCode         = d.Unit!.UnitCode,
                BatchNumber      = d.BatchNumber,
                ExpiryDate       = d.ExpiryDate,
                Quantity         = d.Quantity,
                FreeQuantity     = d.FreeQuantity,
                Rate             = d.Rate,
                LineTotal        = d.LineTotal
            })
            .ToListAsync(ct);

        return PagedResult<PurchaseItemRowDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<PurchaseDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<Purchase>().Query()
            .Where(p => p.PurchaseId == id)
            .Select(p => new PurchaseDto
            {
                PurchaseId            = p.PurchaseId,
                PurchaseNumber        = p.PurchaseNumber,
                PurchaseDate          = p.PurchaseDate,
                SupplierId            = p.SupplierId,
                SupplierName          = p.Supplier!.SupplierName,
                SupplierInvoiceNumber = p.SupplierInvoiceNumber,
                SupplierInvoiceDate   = p.SupplierInvoiceDate,
                PurchaseOrderId       = p.PurchaseOrderId,
                LocationId            = p.LocationId,
                LocationName          = p.Location!.LocationName,
                WarehouseId           = p.WarehouseId,
                WarehouseName         = p.Warehouse != null ? p.Warehouse.WarehouseName : null,
                IsInterState          = p.IsInterState,
                GrossAmount           = p.GrossAmount,
                DiscountAmount        = p.DiscountAmount,
                TaxableAmount         = p.TaxableAmount,
                CgstAmount            = p.CgstAmount,
                SgstAmount            = p.SgstAmount,
                IgstAmount            = p.IgstAmount,
                FreightCharges        = p.FreightCharges,
                OtherCharges          = p.OtherCharges,
                RoundOff              = p.RoundOff,
                GrandTotal            = p.GrandTotal,
                PaidAmount            = p.PaidAmount,
                BalanceAmount         = p.BalanceAmount,
                PaymentStatus         = p.PaymentStatus.ToString(),
                Status                = p.Status,
                DueDate               = p.DueDate,
                Remarks               = p.Remarks,
                PostedAt              = p.PostedAt,
                CancelledAt           = p.CancelledAt,
                CancelReason          = p.CancelReason,
                CreatedAt             = p.CreatedAt,
                LineCount             = p.Details.Count
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Purchase", id);

        dto.Lines = await _uow.Repository<PurchaseDetail>().Query()
            .Where(d => d.PurchaseId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new PurchaseLineDto
            {
                PurchaseDetailId  = d.PurchaseDetailId,
                LineNumber        = d.LineNumber,
                ItemId         = d.ItemId,
                ItemName       = d.Item!.ItemName,
                UnitCode          = d.Unit!.UnitCode,
                BatchId           = d.BatchId,
                BatchNumber       = d.BatchNumber,
                ManufacturingDate = d.ManufacturingDate,
                ExpiryDate        = d.ExpiryDate,
                Quantity          = d.Quantity,
                FreeQuantity      = d.FreeQuantity,
                TotalQuantity     = d.TotalQuantity,
                Rate              = d.Rate,
                GrossAmount       = d.GrossAmount,
                DiscountPercent   = d.DiscountPercent,
                DiscountAmount    = d.DiscountAmount,
                TaxableAmount     = d.TaxableAmount,
                GstPercent        = d.GstPercent,
                CgstAmount        = d.CgstAmount,
                SgstAmount        = d.SgstAmount,
                IgstAmount        = d.IgstAmount,
                LineTotal         = d.LineTotal,
                LandedRate        = d.LandedRate,
                Mrp               = d.Mrp,
                SellingRate       = d.SellingRate,
                HsnCode           = d.HsnCode
            })
            .ToListAsync(ct);

        return dto;
    }

    /* ============================ create ============================ */

    public async Task<PurchasePrintDto> GetPurchaseForPrintAsync(long id, CancellationToken ct = default)
    {
        var purchase = await GetByIdAsync(id, ct);

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

        // HSN/rate-wise tax summary, rolled up from the lines already loaded.
        var taxSummary = purchase.Lines
            .GroupBy(l => new { l.HsnCode, l.GstPercent })
            .Select(g => new InvoiceTaxSummaryDto
            {
                HsnCode       = g.Key.HsnCode,
                GstPercent    = g.Key.GstPercent,
                TaxableAmount = g.Sum(l => l.TaxableAmount),
                CgstAmount    = g.Sum(l => l.CgstAmount),
                SgstAmount    = g.Sum(l => l.SgstAmount),
                IgstAmount    = g.Sum(l => l.IgstAmount),
                TotalTax      = g.Sum(l => l.CgstAmount + l.SgstAmount + l.IgstAmount)
            })
            .OrderBy(s => s.GstPercent)
            .ToList();

        return new PurchasePrintDto
        {
            Shop          = shop,
            Purchase      = purchase,
            TaxSummary    = taxSummary,
            AmountInWords = AmountToWords.Convert(purchase.GrandTotal)
        };
    }

    public async Task<PurchaseDto> CreateAsync(SavePurchaseRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var supplier = await _uow.Repository<Supplier>()
            .FirstOrDefaultAsync(s => s.SupplierId == request.SupplierId && !s.IsDeleted, tracking: false, ct)
            ?? throw new ValidationException(nameof(request.SupplierId), "The selected supplier does not exist.");

        await GuardDuplicateSupplierBillAsync(request.SupplierId, request.SupplierInvoiceNumber, null, ct);
        await GuardWarehouseAsync(request.WarehouseId, ct);

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);
        var isInterState = await IsInterStateAsync(supplier.StateId, ct);

        var purchaseId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var purchase = new Purchase
            {
                PurchaseNumber        = await _numbers.NextAsync(DocumentType.Purchase, token),
                PurchaseDate          = request.PurchaseDate.Date,
                SupplierId            = request.SupplierId,
                SupplierInvoiceNumber = string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber)
                                            ? null : request.SupplierInvoiceNumber.Trim(),
                SupplierInvoiceDate   = request.SupplierInvoiceDate,
                PurchaseOrderId       = request.PurchaseOrderId,
                VoucherId             = await ResolveVoucherIdAsync(VoucherPurchaseGrn, token),
                LocationId            = locationId,
                WarehouseId           = request.WarehouseId,
                IsInterState          = isInterState,
                SupplierStateId       = supplier.StateId,
                FreightCharges        = request.FreightCharges,
                OtherCharges          = request.OtherCharges,
                PaidAmount            = request.PaidAmount,
                Remarks               = request.Remarks,
                Status                = DocumentStatus.Draft,
                // The bill can carry its own credit term; otherwise the
                // supplier's default payment term applies.
                DueDate               = request.PurchaseDate.Date.AddDays(request.CreditDays ?? supplier.PaymentTermDays)
            };

            await _uow.Repository<Purchase>().AddAsync(purchase, token);
            await _uow.SaveChangesAsync(token);

            await BuildLinesAsync(purchase, request, isInterState, token);

            return purchase.PurchaseId;
        }, ct);

        return await GetByIdAsync(purchaseId, ct);
    }

    public async Task<PurchaseDto> UpdateAsync(long id, SavePurchaseRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var supplier = await _uow.Repository<Supplier>()
            .FirstOrDefaultAsync(s => s.SupplierId == request.SupplierId && !s.IsDeleted, tracking: false, ct)
            ?? throw new ValidationException(nameof(request.SupplierId), "The selected supplier does not exist.");

        // Excludes this bill, so re-saving it with the same supplier invoice is fine.
        await GuardDuplicateSupplierBillAsync(request.SupplierId, request.SupplierInvoiceNumber, id, ct);
        await GuardWarehouseAsync(request.WarehouseId, ct);

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);
        var isInterState = await IsInterStateAsync(supplier.StateId, ct);

        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var purchase = await _uow.Repository<Purchase>()
                .FirstOrDefaultAsync(p => p.PurchaseId == id, tracking: true, token)
                ?? throw new NotFoundException("Purchase", id);

            // A GRN can only be re-worked while it is still a draft. Once it is
            // posted the stock has moved and the batches exist, so the correct
            // path is cancel-and-reverse, not silent edit.
            if (purchase.Status != DocumentStatus.Draft)
                throw new BusinessRuleException(
                    $"GRN {purchase.PurchaseNumber} is {purchase.Status} and can no longer be edited.", "NOT_DRAFT");

            purchase.PurchaseDate          = request.PurchaseDate.Date;
            purchase.SupplierId            = request.SupplierId;
            purchase.SupplierInvoiceNumber = string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber)
                                                 ? null : request.SupplierInvoiceNumber.Trim();
            purchase.SupplierInvoiceDate   = request.SupplierInvoiceDate;
            purchase.PurchaseOrderId       = request.PurchaseOrderId;
            purchase.LocationId            = locationId;
            purchase.WarehouseId           = request.WarehouseId;
            purchase.IsInterState          = isInterState;
            purchase.SupplierStateId       = supplier.StateId;
            purchase.FreightCharges        = request.FreightCharges;
            purchase.OtherCharges          = request.OtherCharges;
            purchase.PaidAmount            = request.PaidAmount;
            purchase.Remarks               = request.Remarks;
            purchase.DueDate               = request.PurchaseDate.Date
                                                 .AddDays(request.CreditDays ?? supplier.PaymentTermDays);

            // Nothing has posted yet, so the old lines can be swept and rebuilt
            // wholesale rather than diffed line by line.
            var oldLines = await _uow.Repository<PurchaseDetail>().Query(tracking: true)
                .Where(d => d.PurchaseId == id)
                .ToListAsync(token);
            _uow.Repository<PurchaseDetail>().RemoveRange(oldLines);
            await _uow.SaveChangesAsync(token);

            await BuildLinesAsync(purchase, request, isInterState, token);

            return null;
        }, ct);

        return await GetByIdAsync(id, ct);
    }

    private async Task BuildLinesAsync(
        Purchase purchase, SavePurchaseRequest request, bool isInterState, CancellationToken ct)
    {
        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();

        var items = await _uow.Repository<Item>().Query()
            .Where(p => itemIds.Contains(p.ItemId) && !p.IsDeleted)
            .Select(p => new
            {
                p.ItemId, p.ItemName, p.UnitId,
                GstPercent = p.GstSlab!.TotalRate,
                HsnCode = p.Hsn != null ? p.Hsn.Code : null
            })
            .ToDictionaryAsync(p => p.ItemId, ct);

        var lineNumber = 0;
        var details = new List<PurchaseDetail>();

        foreach (var line in request.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item))
                throw new ValidationException(nameof(line.ItemId), $"Item {line.ItemId} does not exist.");

            if (line.Quantity <= 0)
                throw new ValidationException(nameof(line.Quantity),
                    $"{item.ItemName}: quantity must be greater than zero.");

            var gross = _gst.Money(line.Quantity * line.Rate);

            // A percentage and a flat amount can both arrive; the percentage
            // wins when supplied, so the two cannot silently stack.
            var discount = line.DiscountPercent > 0
                ? _gst.Money(gross * line.DiscountPercent / 100m)
                : _gst.Money(line.DiscountAmount);

            if (discount > gross)
                throw new ValidationException(nameof(line.DiscountAmount),
                    $"{item.ItemName}: discount cannot exceed the line amount.");

            var taxable = gross - discount;
            var tax = _gst.Split(taxable, item.GstPercent, isInterState);

            details.Add(new PurchaseDetail
            {
                PurchaseId        = purchase.PurchaseId,
                LineNumber        = ++lineNumber,
                ItemId         = line.ItemId,
                BatchNumber       = string.IsNullOrWhiteSpace(line.BatchNumber) ? "GEN" : line.BatchNumber.Trim(),
                ManufacturingDate = line.ManufacturingDate,
                ExpiryDate        = line.ExpiryDate,
                Quantity          = line.Quantity,
                FreeQuantity      = line.FreeQuantity,
                UnitId            = item.UnitId,
                Rate              = line.Rate,
                DiscountPercent   = line.DiscountPercent,
                DiscountAmount    = discount,
                GstPercent        = item.GstPercent,
                CgstAmount        = tax.Cgst,
                SgstAmount        = tax.Sgst,
                IgstAmount        = tax.Igst,
                CessAmount        = tax.Cess,
                Mrp               = line.Mrp,
                SellingRate       = line.SellingRate,
                HsnCode           = item.HsnCode,
                Remarks           = line.Remarks
            });
        }

        await _uow.Repository<PurchaseDetail>().AddRangeAsync(details, ct);
        await _uow.SaveChangesAsync(ct);

        await ApplyLandedCostAsync(purchase, ct);
        await RecalculateHeaderAsync(purchase.PurchaseId, ct);
    }

    /// <summary>
    /// Spreads freight and other charges across lines in proportion to taxable
    /// value, then divides by the TOTAL quantity including free goods.
    ///
    /// Both halves matter. Ignoring freight understates cost on every bulky
    /// consignment; dividing by paid quantity alone would ignore that a
    /// "10 + 1 free" scheme is exactly what makes the deal profitable.
    /// GST is excluded because it is input credit, not cost.
    /// </summary>
    private async Task ApplyLandedCostAsync(Purchase purchase, CancellationToken ct)
    {
        var lines = await _uow.Repository<PurchaseDetail>().Query(tracking: true)
            .Where(d => d.PurchaseId == purchase.PurchaseId)
            .ToListAsync(ct);

        if (lines.Count == 0) return;

        // The arithmetic lives in LandedCost so it can be unit-tested without a
        // database - this method only supplies the inputs and stores the result.
        var rates = LandedCost.Apportion(
            lines.Select(l => new CostableLine(l.TaxableAmount, l.Quantity, l.FreeQuantity)).ToList(),
            purchase.FreightCharges + purchase.OtherCharges);

        for (var index = 0; index < lines.Count; index++)
            lines[index].LandedRate = rates[index];

        await _uow.SaveChangesAsync(ct);
    }

    private async Task RecalculateHeaderAsync(long purchaseId, CancellationToken ct)
    {
        var purchase = await _uow.Repository<Purchase>()
            .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId, tracking: true, ct);

        if (purchase is null) return;

        var totals = await _uow.Repository<PurchaseDetail>().Query()
            .Where(d => d.PurchaseId == purchaseId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Gross    = g.Sum(d => d.GrossAmount),
                Discount = g.Sum(d => d.DiscountAmount),
                Taxable  = g.Sum(d => d.TaxableAmount),
                Cgst     = g.Sum(d => d.CgstAmount),
                Sgst     = g.Sum(d => d.SgstAmount),
                Igst     = g.Sum(d => d.IgstAmount),
                Cess     = g.Sum(d => d.CessAmount)
            })
            .FirstOrDefaultAsync(ct);

        purchase.GrossAmount    = totals?.Gross ?? 0m;
        purchase.DiscountAmount = totals?.Discount ?? 0m;
        purchase.TaxableAmount  = totals?.Taxable ?? 0m;
        purchase.CgstAmount     = totals?.Cgst ?? 0m;
        purchase.SgstAmount     = totals?.Sgst ?? 0m;
        purchase.IgstAmount     = totals?.Igst ?? 0m;
        purchase.CessAmount     = totals?.Cess ?? 0m;

        // GrandTotal is a computed column, so the round-off is derived from the
        // components rather than read back from a value the database has not
        // written yet.
        var beforeRounding = purchase.TaxableAmount + purchase.CgstAmount + purchase.SgstAmount
                             + purchase.IgstAmount + purchase.CessAmount
                             + purchase.FreightCharges + purchase.OtherCharges;

        purchase.RoundOff = _gst.RoundOffAdjustment(beforeRounding);

        await _uow.SaveChangesAsync(ct);
    }

    /* ============================ post ============================ */

    public async Task<PurchaseDto> PostAsync(long id, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var purchase = await _uow.Repository<Purchase>()
                .FirstOrDefaultAsync(p => p.PurchaseId == id, tracking: true, token)
                ?? throw new NotFoundException("Purchase", id);

            if (purchase.Status != DocumentStatus.Draft)
                throw new BusinessRuleException(
                    $"Purchase {purchase.PurchaseNumber} is already {purchase.Status}.", "NOT_DRAFT");

            var lines = await _uow.Repository<PurchaseDetail>().Query(tracking: true)
                .Where(d => d.PurchaseId == id)
                .OrderBy(d => d.LineNumber)
                .ToListAsync(token);

            if (lines.Count == 0)
                throw new BusinessRuleException("Cannot post a purchase with no lines.", "NO_LINES");

            var updateItemRates = await GetBoolSettingAsync("Purchase.UpdateItemRate", true, token);

            foreach (var line in lines)
            {
                var batch = await ResolveBatchAsync(line, purchase.LocationId, token);
                line.BatchId = batch.BatchId;

                // Stock comes in at the LANDED rate, not the invoice rate, so
                // batch valuation and later profit both use real cost.
                await _posting.PostAsync(new StockMovement(
                    StockTransactionTypeId.PurchaseIn,
                    purchase.PurchaseDate,
                    line.ItemId,
                    batch.BatchId,
                    purchase.LocationId,
                    line.Quantity + line.FreeQuantity,
                    line.LandedRate,
                    StockReferenceType.Purchase,
                    purchase.PurchaseId,
                    line.PurchaseDetailId,
                    purchase.PurchaseNumber), token);

                if (updateItemRates)
                    await UpdateItemRatesAsync(line, purchase.PurchaseNumber, token);
            }

            await UpdatePurchaseOrderProgressAsync(purchase, lines, token);

            purchase.Status = DocumentStatus.Posted;
            purchase.PostedAt = _clock.UtcNow;

            return null;
        }, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<PurchaseDto> CancelAsync(long id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("reason", "A cancellation reason is required.");

        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var purchase = await _uow.Repository<Purchase>()
                .FirstOrDefaultAsync(p => p.PurchaseId == id, tracking: true, token)
                ?? throw new NotFoundException("Purchase", id);

            if (purchase.Status == DocumentStatus.Cancelled)
                throw new BusinessRuleException("This purchase is already cancelled.", "ALREADY_CANCELLED");

            if (purchase.PaidAmount > 0)
                throw new BusinessRuleException(
                    $"{purchase.PaidAmount:N2} has been paid against this bill. " +
                    "Reverse the payment before cancelling.",
                    "PAYMENT_EXISTS");

            if (purchase.Status == DocumentStatus.Posted)
            {
                // Reversal, not deletion: the goods may already have been sold
                // on, and the journal has to keep showing that they arrived
                // before it shows them going back.
                await _posting.ReverseDocumentAsync(
                    StockReferenceType.Purchase, id,
                    $"Cancellation of {purchase.PurchaseNumber}: {reason}", token);
            }

            purchase.Status = DocumentStatus.Cancelled;
            purchase.CancelledAt = _clock.UtcNow;
            purchase.CancelReason = reason.Trim();

            return null;
        }, ct);

        return await GetByIdAsync(id, ct);
    }

    /* ============================ returns ============================ */

    public async Task<PagedResult<PurchaseReturnDto>> GetReturnsAsync(
        PurchaseReturnQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<PurchaseReturn>().Query()
            .WhereIf(parameters.SupplierId.HasValue, r => r.SupplierId == parameters.SupplierId)
            .WhereIf(parameters.FromDate.HasValue, r => r.ReturnDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, r => r.ReturnDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.Status.HasValue, r => r.Status == parameters.Status!.Value)
            .WhereIf(search is not null, r => r.ReturnNumber.Contains(search!));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<PurchaseReturnDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.PurchaseReturnId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(ReturnProjection)
            .ToListAsync(ct);

        return PagedResult<PurchaseReturnDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<PurchaseReturnDto> GetReturnAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<PurchaseReturn>().Query()
            .Where(r => r.PurchaseReturnId == id)
            .Select(ReturnProjection)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Purchase return", id);

        dto.Lines = await _uow.Repository<PurchaseReturnDetail>().Query()
            .Where(d => d.PurchaseReturnId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new PurchaseReturnLineDto
            {
                PurchaseReturnDetailId = d.PurchaseReturnDetailId,
                ItemId     = d.ItemId,
                ItemName   = d.Item!.ItemName,
                BatchId       = d.BatchId,
                BatchNumber   = d.Batch!.BatchNumber,
                Quantity      = d.Quantity,
                Rate          = d.Rate,
                TaxableAmount = d.TaxableAmount,
                GstPercent    = d.GstPercent,
                LineTotal     = d.LineTotal,
                ReturnReason  = d.ReturnReason
            })
            .ToListAsync(ct);

        return dto;
    }

    /// <summary>
    /// An Expression rather than a method, for the same reason as
    /// PaymentService.PaymentProjection: a method inside Select() is evaluated
    /// on the client against un-Included navigations and blows up on
    /// r.Supplier.
    /// </summary>
    private static readonly Expression<Func<PurchaseReturn, PurchaseReturnDto>> ReturnProjection = r => new PurchaseReturnDto
    {
        PurchaseReturnId = r.PurchaseReturnId,
        ReturnNumber     = r.ReturnNumber,
        ReturnDate       = r.ReturnDate,
        SupplierId       = r.SupplierId,
        SupplierName     = r.Supplier!.SupplierName,
        PurchaseId       = r.PurchaseId,
        DebitNoteNumber  = r.DebitNoteNumber,
        ReturnReason     = r.ReturnReason,
        TaxableAmount    = r.TaxableAmount,
        CgstAmount       = r.CgstAmount,
        SgstAmount       = r.SgstAmount,
        IgstAmount       = r.IgstAmount,
        GrandTotal       = r.GrandTotal,
        Status           = r.Status,
        PostedAt         = r.PostedAt
    };

    public async Task<PurchaseReturnDto> CreateReturnAsync(
        SavePurchaseReturnRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var supplier = await _uow.Repository<Supplier>()
            .FirstOrDefaultAsync(s => s.SupplierId == request.SupplierId && !s.IsDeleted, tracking: false, ct)
            ?? throw new ValidationException(nameof(request.SupplierId), "The selected supplier does not exist.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);
        var isInterState = await IsInterStateAsync(supplier.StateId, ct);

        var returnId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var purchaseReturn = new PurchaseReturn
            {
                ReturnNumber    = await _numbers.NextAsync(DocumentType.PurchaseReturn, token),
                ReturnDate      = request.ReturnDate.Date,
                SupplierId      = request.SupplierId,
                PurchaseId      = request.PurchaseId,
                LocationId      = locationId,
                DebitNoteNumber = request.DebitNoteNumber,
                ReturnReason    = request.ReturnReason,
                IsInterState    = isInterState,
                Remarks         = request.Remarks,
                Status          = DocumentStatus.Draft
            };

            await _uow.Repository<PurchaseReturn>().AddAsync(purchaseReturn, token);
            await _uow.SaveChangesAsync(token);

            var lineNumber = 0;
            var details = new List<PurchaseReturnDetail>();

            foreach (var line in request.Lines)
            {
                var batch = await _uow.Repository<ItemBatch>().Query()
                    .Where(b => b.BatchId == line.BatchId)
                    .Select(b => new
                    {
                        b.BatchId, b.ItemId, b.BatchNumber, b.CurrentQty,
                        ItemName = b.Item!.ItemName,
                        UnitId = b.Item.UnitId,
                        GstPercent = b.Item.GstSlab!.TotalRate
                    })
                    .FirstOrDefaultAsync(token)
                    ?? throw new NotFoundException("Batch", line.BatchId);

                if (line.Quantity <= 0)
                    throw new ValidationException(nameof(line.Quantity),
                        $"{batch.ItemName}: return quantity must be greater than zero.");

                // Checked here rather than only at posting, so the operator
                // learns immediately instead of after filling in the whole note.
                if (line.Quantity > batch.CurrentQty)
                    throw new BusinessRuleException(
                        $"{batch.ItemName}: batch {batch.BatchNumber} holds {batch.CurrentQty:N3}, " +
                        $"cannot return {line.Quantity:N3}.",
                        "INSUFFICIENT_STOCK");

                var gross = _gst.Money(line.Quantity * line.Rate);
                var taxable = gross - _gst.Money(line.DiscountAmount);
                var tax = _gst.Split(taxable, batch.GstPercent, isInterState);

                details.Add(new PurchaseReturnDetail
                {
                    PurchaseReturnId = purchaseReturn.PurchaseReturnId,
                    LineNumber       = ++lineNumber,
                    ItemId        = batch.ItemId,
                    BatchId          = batch.BatchId,
                    PurchaseDetailId = line.PurchaseDetailId,
                    Quantity         = line.Quantity,
                    UnitId           = batch.UnitId,
                    Rate             = line.Rate,
                    DiscountAmount   = _gst.Money(line.DiscountAmount),
                    GstPercent       = batch.GstPercent,
                    CgstAmount       = tax.Cgst,
                    SgstAmount       = tax.Sgst,
                    IgstAmount       = tax.Igst,
                    CessAmount       = tax.Cess,
                    ReturnReason     = line.ReturnReason
                });
            }

            await _uow.Repository<PurchaseReturnDetail>().AddRangeAsync(details, token);
            await _uow.SaveChangesAsync(token);

            purchaseReturn.GrossAmount    = details.Sum(d => d.GrossAmount);
            purchaseReturn.DiscountAmount = details.Sum(d => d.DiscountAmount);
            purchaseReturn.TaxableAmount  = details.Sum(d => d.TaxableAmount);
            purchaseReturn.CgstAmount     = details.Sum(d => d.CgstAmount);
            purchaseReturn.SgstAmount     = details.Sum(d => d.SgstAmount);
            purchaseReturn.IgstAmount     = details.Sum(d => d.IgstAmount);
            purchaseReturn.CessAmount     = details.Sum(d => d.CessAmount);

            return purchaseReturn.PurchaseReturnId;
        }, ct);

        return await GetReturnAsync(returnId, ct);
    }

    public async Task<PurchaseReturnDto> PostReturnAsync(long id, CancellationToken ct = default)
    {
        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var purchaseReturn = await _uow.Repository<PurchaseReturn>()
                .FirstOrDefaultAsync(r => r.PurchaseReturnId == id, tracking: true, token)
                ?? throw new NotFoundException("Purchase return", id);

            if (purchaseReturn.Status != DocumentStatus.Draft)
                throw new BusinessRuleException(
                    $"Return {purchaseReturn.ReturnNumber} is already {purchaseReturn.Status}.", "NOT_DRAFT");

            var lines = await _uow.Repository<PurchaseReturnDetail>().Query()
                .Where(d => d.PurchaseReturnId == id)
                .ToListAsync(token);

            foreach (var line in lines)
            {
                await _posting.PostAsync(new StockMovement(
                    StockTransactionTypeId.PurchaseReturnOut,
                    purchaseReturn.ReturnDate,
                    line.ItemId,
                    line.BatchId,
                    purchaseReturn.LocationId,
                    line.Quantity,
                    line.Rate,
                    StockReferenceType.PurchaseReturn,
                    purchaseReturn.PurchaseReturnId,
                    line.PurchaseReturnDetailId,
                    purchaseReturn.ReturnNumber,
                    line.ReturnReason), token);
            }

            purchaseReturn.Status = DocumentStatus.Posted;
            purchaseReturn.PostedAt = _clock.UtcNow;

            return null;
        }, ct);

        return await GetReturnAsync(id, ct);
    }

    /* ============================ orders ============================ */

    public async Task<PagedResult<PurchaseOrderDto>> GetOrdersAsync(
        PurchaseOrderQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<PurchaseOrder>().Query()
            .WhereIf(parameters.SupplierId.HasValue, o => o.SupplierId == parameters.SupplierId)
            .WhereIf(parameters.Status.HasValue, o => o.Status == parameters.Status!.Value)
            .WhereIf(parameters.FromDate.HasValue, o => o.OrderDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, o => o.OrderDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.PendingOnly == true,
                     o => o.Status == PurchaseOrderStatus.Open || o.Status == PurchaseOrderStatus.Partial)
            .WhereIf(search is not null, o =>
                o.OrderNumber.Contains(search!) || o.Supplier!.SupplierName.Contains(search!));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<PurchaseOrderDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.PurchaseOrderId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(o => new PurchaseOrderDto
            {
                PurchaseOrderId = o.PurchaseOrderId,
                OrderNumber     = o.OrderNumber,
                OrderDate       = o.OrderDate,
                ExpectedDate    = o.ExpectedDate,
                SupplierId      = o.SupplierId,
                SupplierName    = o.Supplier!.SupplierName,
                TotalQty        = o.TotalQty,
                EstimatedValue  = o.EstimatedValue,
                Status          = o.Status,
                Remarks         = o.Remarks
            })
            .ToListAsync(ct);

        return PagedResult<PurchaseOrderDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    /// <summary>
    /// The order list flattened to one row PER LINE ITEM (item-wise view): a
    /// 5-item order returns 5 rows, each carrying its parent order's number,
    /// date, supplier and status. Same filters and paging as GetOrdersAsync,
    /// but paging is over lines, not orders.
    /// </summary>
    public async Task<PagedResult<PurchaseOrderItemRowDto>> GetOrderItemsAsync(
        PurchaseOrderQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<PurchaseOrderDetail>().Query()
            .WhereIf(parameters.SupplierId.HasValue, d => d.PurchaseOrder!.SupplierId == parameters.SupplierId)
            .WhereIf(parameters.Status.HasValue, d => d.PurchaseOrder!.Status == parameters.Status!.Value)
            .WhereIf(parameters.FromDate.HasValue, d => d.PurchaseOrder!.OrderDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, d => d.PurchaseOrder!.OrderDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.PendingOnly == true,
                     d => d.PurchaseOrder!.Status == PurchaseOrderStatus.Open
                       || d.PurchaseOrder!.Status == PurchaseOrderStatus.Partial)
            .WhereIf(search is not null, d =>
                d.PurchaseOrder!.OrderNumber.Contains(search!)
                || d.PurchaseOrder!.Supplier!.SupplierName.Contains(search!)
                || d.Item!.ItemName.Contains(search!)
                || d.Item!.ItemCode.Contains(search!));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<PurchaseOrderItemRowDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(d => d.PurchaseOrder!.OrderDate)
            .ThenByDescending(d => d.PurchaseOrderId)
            .ThenBy(d => d.LineNumber)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(d => new PurchaseOrderItemRowDto
            {
                PurchaseOrderId  = d.PurchaseOrderId,
                OrderNumber      = d.PurchaseOrder!.OrderNumber,
                OrderDate        = d.PurchaseOrder.OrderDate,
                ExpectedDate     = d.PurchaseOrder.ExpectedDate,
                SupplierId       = d.PurchaseOrder.SupplierId,
                SupplierName     = d.PurchaseOrder.Supplier!.SupplierName,
                Status           = d.PurchaseOrder.Status,
                PurchaseOrderDetailId = d.PurchaseOrderDetailId,
                ItemCode         = d.Item!.ItemCode,
                ItemName         = d.Item.ItemName,
                ItemGroupName    = d.Item.ItemGroup!.ItemGroupName,
                ItemSubGroupName = d.Item.ItemSubGroup!.ItemSubGroupName,
                UnitCode         = d.Unit!.UnitCode,
                OrderedQty       = d.OrderedQty,
                Rate             = d.Rate,
                EstimatedAmount  = d.EstimatedAmount
            })
            .ToListAsync(ct);

        return PagedResult<PurchaseOrderItemRowDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<PurchaseOrderDto> GetOrderAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<PurchaseOrder>().Query()
            .Where(o => o.PurchaseOrderId == id)
            .Select(o => new PurchaseOrderDto
            {
                PurchaseOrderId = o.PurchaseOrderId,
                OrderNumber     = o.OrderNumber,
                OrderDate       = o.OrderDate,
                ExpectedDate    = o.ExpectedDate,
                SupplierId      = o.SupplierId,
                SupplierName    = o.Supplier!.SupplierName,
                TotalQty        = o.TotalQty,
                EstimatedValue  = o.EstimatedValue,
                Status          = o.Status,
                Remarks         = o.Remarks
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Purchase order", id);

        dto.Lines = await _uow.Repository<PurchaseOrderDetail>().Query()
            .Where(d => d.PurchaseOrderId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new PurchaseOrderLineDto
            {
                PurchaseOrderDetailId = d.PurchaseOrderDetailId,
                ItemId       = d.ItemId,
                ItemCode        = d.Item!.ItemCode,
                ItemGroupName    = d.Item.ItemGroup!.ItemGroupName,
                ItemSubGroupName = d.Item.ItemSubGroup!.ItemSubGroupName,
                ItemName     = d.Item.ItemName,
                UnitId          = d.UnitId,
                UnitCode        = d.Unit!.UnitCode,
                OrderedQty      = d.OrderedQty,
                ReceivedQty     = d.ReceivedQty,
                PendingQty      = d.PendingQty,
                Rate            = d.Rate,
                EstimatedAmount = d.EstimatedAmount,
                NoOfPacks       = d.NoOfPacks,
                QtyPerPack      = d.QtyPerPack,
                RequiredQty     = d.RequiredQty,
                Remarks         = d.Remarks,
                ItemRemark      = d.ItemRemark,
                RequisitionDetailId = d.RequisitionDetailId,
                GstPercent      = d.Item.GstSlab!.TotalRate,
                HsnCode         = d.Item.Hsn != null ? d.Item.Hsn.Code : null,
                Mrp             = d.Item.Mrp,
                SellingRate     = d.Item.SellingRate
            })
            .ToListAsync(ct);

        return dto;
    }

    public async Task<PurchaseOrderPrintDto> GetOrderForPrintAsync(long id, CancellationToken ct = default)
    {
        // The header + lines are already assembled (item code, group/sub-group,
        // pack breakdown, GST slab and HSN all come along) - reuse them.
        var order = await GetOrderAsync(id, ct);

        // ---- Shop letterhead: ShopMaster first, CompanyProfile fills the gaps --
        var (shop, shopStateId) = await ShopHeaderBuilder.BuildAsync(_uow, ct);

        // ---- Supplier block ---------------------------------------------------
        var sup = await _uow.Repository<Supplier>().Query()
            .Where(s => s.SupplierId == order.SupplierId)
            .Select(s => new
            {
                s.SupplierCode,
                s.SupplierName,
                s.GstNumber,
                s.Address,
                s.City,
                StateName = s.State != null ? s.State.StateName : null,
                s.StateId,
                s.Pincode,
                s.Phone,
                s.Email,
                s.ContactPerson,
                s.PaymentTermDays
            })
            .FirstOrDefaultAsync(ct);

        var supplier = sup == null
            ? new PurchaseOrderSupplierDto { SupplierName = order.SupplierName }
            : new PurchaseOrderSupplierDto
            {
                SupplierCode    = sup.SupplierCode,
                SupplierName    = sup.SupplierName,
                GstNumber       = sup.GstNumber,
                Address         = sup.Address,
                City            = sup.City,
                StateName       = sup.StateName,
                Pincode         = sup.Pincode,
                Phone           = sup.Phone,
                Email           = sup.Email,
                ContactPerson   = sup.ContactPerson,
                PaymentTermDays = sup.PaymentTermDays
            };

        var deliveryLocation = await _uow.Repository<PurchaseOrder>().Query()
            .Where(o => o.PurchaseOrderId == id)
            .Select(o => o.Location != null ? o.Location.LocationName : null)
            .FirstOrDefaultAsync(ct);

        // ---- Estimated GST ----------------------------------------------------
        // A PO is a booking, so tax here is an estimate off each line's slab.
        // Same state as the shop -> CGST+SGST; a different state -> IGST.
        var interState = sup?.StateId is int ss && shopStateId is int shs && ss != shs;

        var taxSummary = order.Lines
            .GroupBy(l => new { l.HsnCode, l.GstPercent })
            .Select(g =>
            {
                var taxable = g.Sum(l => l.EstimatedAmount);
                var tax = Math.Round(taxable * g.Key.GstPercent / 100m, 2, MidpointRounding.AwayFromZero);
                var half = Math.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
                return new InvoiceTaxSummaryDto
                {
                    HsnCode       = g.Key.HsnCode,
                    GstPercent    = g.Key.GstPercent,
                    TaxableAmount = taxable,
                    CgstAmount    = interState ? 0m : half,
                    SgstAmount    = interState ? 0m : tax - half,
                    IgstAmount    = interState ? tax : 0m,
                    TotalTax      = tax
                };
            })
            .OrderBy(s => s.GstPercent)
            .ToList();

        var subTotal = order.Lines.Sum(l => l.EstimatedAmount);
        var cgst = taxSummary.Sum(s => s.CgstAmount);
        var sgst = taxSummary.Sum(s => s.SgstAmount);
        var igst = taxSummary.Sum(s => s.IgstAmount);
        var grandRaw = subTotal + cgst + sgst + igst;
        var grand = Math.Round(grandRaw, 0, MidpointRounding.AwayFromZero);

        return new PurchaseOrderPrintDto
        {
            Shop                 = shop,
            Order                = order,
            Supplier             = supplier,
            DeliveryLocationName = deliveryLocation,
            IsInterState         = interState,
            TaxSummary           = taxSummary,
            TotalPacks           = (int)Math.Round(order.Lines.Sum(l => l.NoOfPacks), 0, MidpointRounding.AwayFromZero),
            SubTotal             = subTotal,
            TaxableAmount        = subTotal,
            CgstAmount           = cgst,
            SgstAmount           = sgst,
            IgstAmount           = igst,
            RoundOff             = grand - grandRaw,
            GrandTotal           = grand,
            AmountInWords        = AmountToWords.Convert(grand)
        };
    }

    public async Task<PurchaseOrderDto> CreateOrderAsync(
        SavePurchaseOrderRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);

        var order = new PurchaseOrder
        {
            OrderNumber  = await _numbers.NextAsync(DocumentType.PurchaseOrder, ct),
            OrderDate    = request.OrderDate.Date,
            ExpectedDate = request.ExpectedDate,
            SupplierId   = request.SupplierId,
            VoucherId    = await ResolveVoucherIdAsync(VoucherPurchaseOrder, ct),
            LocationId   = locationId,
            Remarks      = request.Remarks,
            // Open, not Draft: an order that has been raised is one the shop is
            // waiting on, and it should appear on the pending screen at once.
            Status       = PurchaseOrderStatus.Open
        };

        await _uow.Repository<PurchaseOrder>().AddAsync(order, ct);
        await _uow.SaveChangesAsync(ct);

        var lineNumber = 0;
        decimal totalQty = 0m, estimatedValue = 0m;
        // Requisition lines this order consumes, so their OrderedQty can be advanced.
        var consumed = new List<(long RequisitionDetailId, decimal Qty)>();

        foreach (var line in request.Lines)
        {
            var item = await _uow.Repository<Item>()
                .FirstOrDefaultAsync(p => p.ItemId == line.ItemId && !p.IsDeleted, tracking: false, ct)
                ?? throw new ValidationException(nameof(line.ItemId), $"Item {line.ItemId} does not exist.");

            if (line.OrderedQty <= 0)
                throw new ValidationException(nameof(line.OrderedQty),
                    $"{item.ItemName}: ordered quantity must be greater than zero.");

            await _uow.Repository<PurchaseOrderDetail>().AddAsync(new PurchaseOrderDetail
            {
                PurchaseOrderId = order.PurchaseOrderId,
                LineNumber      = ++lineNumber,
                ItemId       = line.ItemId,
                OrderedQty      = line.OrderedQty,
                UnitId          = line.UnitId ?? item.UnitId,
                Rate            = line.Rate > 0 ? line.Rate : item.PurchaseRate,
                NoOfPacks       = line.NoOfPacks,
                QtyPerPack      = line.QtyPerPack,
                RequiredQty     = line.RequiredQty,
                Remarks         = line.Remarks,
                ItemRemark      = line.ItemRemark,
                RequisitionDetailId = line.RequisitionDetailId
            }, ct);

            if (line.RequisitionDetailId is { } reqDetailId)
                consumed.Add((reqDetailId, line.OrderedQty));

            totalQty += line.OrderedQty;
            estimatedValue += line.OrderedQty * (line.Rate > 0 ? line.Rate : item.PurchaseRate);
        }

        order.TotalQty = totalQty;
        order.EstimatedValue = _gst.Money(estimatedValue);
        await _uow.SaveChangesAsync(ct);

        await AdjustRequisitionProgressAsync(consumed, ct);

        return await GetOrderAsync(order.PurchaseOrderId, ct);
    }

    public async Task<PurchaseOrderDto> UpdateOrderAsync(
        long id, SavePurchaseOrderRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var supplier = await _uow.Repository<Supplier>()
            .FirstOrDefaultAsync(s => s.SupplierId == request.SupplierId && !s.IsDeleted, tracking: false, ct)
            ?? throw new ValidationException(nameof(request.SupplierId), "The selected supplier does not exist.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);

        // Signed requisition adjustments: the old lines are handed back to their
        // requisitions (negative) and the new lines are drawn afresh (positive),
        // netted per requisition line so a status only settles once.
        var deltas = new Dictionary<long, decimal>();

        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var order = await _uow.Repository<PurchaseOrder>()
                .FirstOrDefaultAsync(o => o.PurchaseOrderId == id, tracking: true, token)
                ?? throw new NotFoundException("Purchase order", id);

            // Editable only while nothing has been received against it. Once a
            // GRN draws on the order it is Partial/Received and frozen.
            if (order.Status != PurchaseOrderStatus.Open)
                throw new BusinessRuleException(
                    $"Order {order.OrderNumber} is {order.Status} and can no longer be edited.", "NOT_OPEN");

            var oldLines = await _uow.Repository<PurchaseOrderDetail>().Query(tracking: true)
                .Where(d => d.PurchaseOrderId == id)
                .ToListAsync(token);

            foreach (var old in oldLines)
                if (old.RequisitionDetailId is { } reqId)
                    deltas[reqId] = deltas.GetValueOrDefault(reqId) - old.OrderedQty;

            _uow.Repository<PurchaseOrderDetail>().RemoveRange(oldLines);
            await _uow.SaveChangesAsync(token);

            order.OrderDate    = request.OrderDate.Date;
            order.ExpectedDate = request.ExpectedDate;
            order.SupplierId   = request.SupplierId;
            order.LocationId   = locationId;
            order.Remarks      = request.Remarks;

            var lineNumber = 0;
            decimal totalQty = 0m, estimatedValue = 0m;

            foreach (var line in request.Lines)
            {
                var item = await _uow.Repository<Item>()
                    .FirstOrDefaultAsync(p => p.ItemId == line.ItemId && !p.IsDeleted, tracking: false, token)
                    ?? throw new ValidationException(nameof(line.ItemId), $"Item {line.ItemId} does not exist.");

                if (line.OrderedQty <= 0)
                    throw new ValidationException(nameof(line.OrderedQty),
                        $"{item.ItemName}: ordered quantity must be greater than zero.");

                var rate = line.Rate > 0 ? line.Rate : item.PurchaseRate;

                await _uow.Repository<PurchaseOrderDetail>().AddAsync(new PurchaseOrderDetail
                {
                    PurchaseOrderId     = order.PurchaseOrderId,
                    LineNumber          = ++lineNumber,
                    ItemId              = line.ItemId,
                    OrderedQty          = line.OrderedQty,
                    UnitId              = line.UnitId ?? item.UnitId,
                    Rate                = rate,
                    NoOfPacks           = line.NoOfPacks,
                    QtyPerPack          = line.QtyPerPack,
                    RequiredQty         = line.RequiredQty,
                    Remarks             = line.Remarks,
                    ItemRemark          = line.ItemRemark,
                    RequisitionDetailId = line.RequisitionDetailId
                }, token);

                if (line.RequisitionDetailId is { } reqId)
                    deltas[reqId] = deltas.GetValueOrDefault(reqId) + line.OrderedQty;

                totalQty += line.OrderedQty;
                estimatedValue += line.OrderedQty * rate;
            }

            order.TotalQty = totalQty;
            order.EstimatedValue = _gst.Money(estimatedValue);
            await _uow.SaveChangesAsync(token);

            return null;
        }, ct);

        await AdjustRequisitionProgressAsync(
            deltas.Where(kv => kv.Value != 0m).Select(kv => (kv.Key, kv.Value)).ToList(), ct);

        return await GetOrderAsync(id, ct);
    }

    /* ========================= requisitions ========================= */

    public async Task<PagedResult<PurchaseRequisitionDto>> GetRequisitionsAsync(
        PurchaseRequisitionQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<PurchaseRequisition>().Query()
            .WhereIf(parameters.Status.HasValue, r => r.Status == parameters.Status!.Value)
            .WhereIf(parameters.FromDate.HasValue, r => r.RequisitionDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, r => r.RequisitionDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.PendingOnly == true,
                     r => r.Status == PurchaseRequisitionStatus.Open || r.Status == PurchaseRequisitionStatus.Partial)
            .WhereIf(search is not null, r => r.RequisitionNumber.Contains(search!));

        var totalCount = await query.CountAsync(ct);
        if (totalCount == 0)
            return PagedResult<PurchaseRequisitionDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(r => r.RequisitionDate).ThenByDescending(r => r.RequisitionId)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(r => new PurchaseRequisitionDto
            {
                RequisitionId     = r.RequisitionId,
                RequisitionNumber = r.RequisitionNumber,
                RequisitionDate   = r.RequisitionDate,
                LocationId        = r.LocationId,
                LocationName      = r.Location!.LocationName,
                TotalQty          = r.TotalQty,
                Status            = r.Status,
                Remarks           = r.Remarks,
                ItemNames         = r.Details.Select(d => d.Item!.ItemName).ToList()
            })
            .ToListAsync(ct);

        return PagedResult<PurchaseRequisitionDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    /// <summary>
    /// The requisition list flattened to one row PER LINE ITEM (item-wise view):
    /// a 5-item requisition returns 5 rows, each carrying its parent's number,
    /// date, location and status. Same filters/paging as GetRequisitionsAsync.
    /// </summary>
    public async Task<PagedResult<PurchaseRequisitionItemRowDto>> GetRequisitionItemsAsync(
        PurchaseRequisitionQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<PurchaseRequisitionDetail>().Query()
            .WhereIf(parameters.Status.HasValue, d => d.Requisition!.Status == parameters.Status!.Value)
            .WhereIf(parameters.FromDate.HasValue, d => d.Requisition!.RequisitionDate >= parameters.FromDate!.Value.Date)
            .WhereIf(parameters.ToDate.HasValue, d => d.Requisition!.RequisitionDate <= parameters.ToDate!.Value.Date)
            .WhereIf(parameters.PendingOnly == true,
                     d => d.Requisition!.Status == PurchaseRequisitionStatus.Open
                       || d.Requisition!.Status == PurchaseRequisitionStatus.Partial)
            .WhereIf(search is not null, d =>
                d.Requisition!.RequisitionNumber.Contains(search!)
                || d.Item!.ItemName.Contains(search!)
                || d.Item!.ItemCode.Contains(search!));

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<PurchaseRequisitionItemRowDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .OrderByDescending(d => d.Requisition!.RequisitionDate)
            .ThenByDescending(d => d.RequisitionId)
            .ThenBy(d => d.LineNumber)
            .Skip(parameters.Skip).Take(parameters.PageSize)
            .Select(d => new PurchaseRequisitionItemRowDto
            {
                RequisitionId       = d.RequisitionId,
                RequisitionNumber   = d.Requisition!.RequisitionNumber,
                RequisitionDate     = d.Requisition.RequisitionDate,
                LocationId          = d.Requisition.LocationId,
                LocationName        = d.Requisition.Location!.LocationName,
                Status              = d.Requisition.Status,
                RequisitionDetailId = d.RequisitionDetailId,
                ItemCode            = d.Item!.ItemCode,
                ItemName            = d.Item.ItemName,
                ItemGroupName       = d.Item.ItemGroup!.ItemGroupName,
                ItemSubGroupName    = d.Item.ItemSubGroup!.ItemSubGroupName,
                UnitCode            = d.Unit!.UnitCode,
                RequiredQty         = d.RequiredQty,
                PendingQty          = d.PendingQty,
                EstimatedRate       = d.EstimatedRate,
                EstimatedAmount     = d.RequiredQty * d.EstimatedRate
            })
            .ToListAsync(ct);

        return PagedResult<PurchaseRequisitionItemRowDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    /// <summary>The number the next requisition would get, for showing on the create form.</summary>
    public Task<string?> PeekNextRequisitionNumberAsync(CancellationToken ct = default)
        => _numbers.PeekNextAsync(DocumentType.PurchaseRequisition, ct);

    /// <summary>The number the next purchase order would get, for showing on the create form.</summary>
    public Task<string?> PeekNextOrderNumberAsync(CancellationToken ct = default)
        => _numbers.PeekNextAsync(DocumentType.PurchaseOrder, ct);

    public async Task<PurchaseRequisitionDto> GetRequisitionAsync(long id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<PurchaseRequisition>().Query()
            .Where(r => r.RequisitionId == id)
            .Select(r => new PurchaseRequisitionDto
            {
                RequisitionId     = r.RequisitionId,
                RequisitionNumber = r.RequisitionNumber,
                RequisitionDate   = r.RequisitionDate,
                LocationId        = r.LocationId,
                LocationName      = r.Location!.LocationName,
                TotalQty          = r.TotalQty,
                Status            = r.Status,
                Remarks           = r.Remarks
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Purchase requisition", id);

        dto.Lines = await _uow.Repository<PurchaseRequisitionDetail>().Query()
            .Where(d => d.RequisitionId == id)
            .OrderBy(d => d.LineNumber)
            .Select(d => new PurchaseRequisitionLineDto
            {
                RequisitionDetailId = d.RequisitionDetailId,
                ItemId        = d.ItemId,
                ItemCode         = d.Item!.ItemCode,
                ItemGroupName    = d.Item.ItemGroup!.ItemGroupName,
                ItemSubGroupName = d.Item.ItemSubGroup!.ItemSubGroupName,
                ItemName      = d.Item.ItemName,
                UnitId        = d.UnitId,
                UnitCode      = d.Unit!.UnitCode,
                RequiredQty   = d.RequiredQty,
                OrderedQty    = d.OrderedQty,
                PendingQty    = d.PendingQty,
                ExpectedDate  = d.ExpectedDate,
                EstimatedRate = d.EstimatedRate,
                Remarks       = d.Remarks,
                GstPercent    = d.Item.GstSlab!.TotalRate,
                HsnCode       = d.Item.Hsn != null ? d.Item.Hsn.Code : null,
                Mrp           = d.Item.Mrp,
                SellingRate   = d.Item.SellingRate
            })
            .ToListAsync(ct);

        return dto;
    }

    public async Task<PurchaseRequisitionDto> CreateRequisitionAsync(
        SavePurchaseRequisitionRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);

        var requisition = new PurchaseRequisition
        {
            RequisitionNumber = await _numbers.NextAsync(DocumentType.PurchaseRequisition, ct),
            RequisitionDate   = request.RequisitionDate.Date,
            LocationId        = locationId,
            VoucherId         = await ResolveVoucherIdAsync(VoucherPurchaseRequisition, ct),
            Remarks           = request.Remarks,
            Status            = PurchaseRequisitionStatus.Open
        };

        await _uow.Repository<PurchaseRequisition>().AddAsync(requisition, ct);
        await _uow.SaveChangesAsync(ct);

        var lineNumber = 0;
        decimal totalQty = 0m;

        foreach (var line in request.Lines)
        {
            var item = await _uow.Repository<Item>()
                .FirstOrDefaultAsync(p => p.ItemId == line.ItemId && !p.IsDeleted, tracking: false, ct)
                ?? throw new ValidationException(nameof(line.ItemId), $"Item {line.ItemId} does not exist.");

            if (line.RequiredQty <= 0)
                throw new ValidationException(nameof(line.RequiredQty),
                    $"{item.ItemName}: required quantity must be greater than zero.");

            await _uow.Repository<PurchaseRequisitionDetail>().AddAsync(new PurchaseRequisitionDetail
            {
                RequisitionId = requisition.RequisitionId,
                LineNumber    = ++lineNumber,
                ItemId        = line.ItemId,
                RequiredQty   = line.RequiredQty,
                UnitId        = line.UnitId ?? item.UnitId,
                ExpectedDate  = line.ExpectedDate,
                EstimatedRate = line.EstimatedRate > 0 ? line.EstimatedRate : item.PurchaseRate,
                Remarks       = line.Remarks
            }, ct);

            totalQty += line.RequiredQty;
        }

        requisition.TotalQty = totalQty;
        await _uow.SaveChangesAsync(ct);

        return await GetRequisitionAsync(requisition.RequisitionId, ct);
    }

    public async Task<PurchaseRequisitionDto> UpdateRequisitionAsync(
        long id, SavePurchaseRequisitionRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new ValidationException(nameof(request.Lines), "Add at least one line.");

        var locationId = request.LocationId ?? await GetDefaultLocationIdAsync(ct);

        await _uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var requisition = await _uow.Repository<PurchaseRequisition>()
                .FirstOrDefaultAsync(r => r.RequisitionId == id, tracking: true, token)
                ?? throw new NotFoundException("Purchase requisition", id);

            // Editable only while untouched. The moment a PO draws on it the
            // status is Partial/Converted and its lines carry OrderedQty that an
            // edit must not silently discard.
            if (requisition.Status != PurchaseRequisitionStatus.Open)
                throw new BusinessRuleException(
                    $"Requisition {requisition.RequisitionNumber} is {requisition.Status} and can no longer be edited.",
                    "NOT_OPEN");

            requisition.RequisitionDate = request.RequisitionDate.Date;
            requisition.LocationId      = locationId;
            requisition.Remarks         = request.Remarks;

            // Open guarantees every line is OrderedQty 0, so a wholesale rebuild
            // loses no procurement progress.
            var oldLines = await _uow.Repository<PurchaseRequisitionDetail>().Query(tracking: true)
                .Where(d => d.RequisitionId == id)
                .ToListAsync(token);
            _uow.Repository<PurchaseRequisitionDetail>().RemoveRange(oldLines);
            await _uow.SaveChangesAsync(token);

            var lineNumber = 0;
            decimal totalQty = 0m;

            foreach (var line in request.Lines)
            {
                var item = await _uow.Repository<Item>()
                    .FirstOrDefaultAsync(p => p.ItemId == line.ItemId && !p.IsDeleted, tracking: false, token)
                    ?? throw new ValidationException(nameof(line.ItemId), $"Item {line.ItemId} does not exist.");

                if (line.RequiredQty <= 0)
                    throw new ValidationException(nameof(line.RequiredQty),
                        $"{item.ItemName}: required quantity must be greater than zero.");

                await _uow.Repository<PurchaseRequisitionDetail>().AddAsync(new PurchaseRequisitionDetail
                {
                    RequisitionId = requisition.RequisitionId,
                    LineNumber    = ++lineNumber,
                    ItemId        = line.ItemId,
                    RequiredQty   = line.RequiredQty,
                    UnitId        = line.UnitId ?? item.UnitId,
                    ExpectedDate  = line.ExpectedDate,
                    EstimatedRate = line.EstimatedRate > 0 ? line.EstimatedRate : item.PurchaseRate,
                    Remarks       = line.Remarks
                }, token);

                totalQty += line.RequiredQty;
            }

            requisition.TotalQty = totalQty;
            await _uow.SaveChangesAsync(token);

            return null;
        }, ct);

        return await GetRequisitionAsync(id, ct);
    }

    public async Task<PurchaseRequisitionDto> CancelRequisitionAsync(long id, CancellationToken ct = default)
    {
        var requisition = await _uow.Repository<PurchaseRequisition>()
            .FirstOrDefaultAsync(r => r.RequisitionId == id, tracking: true, ct)
            ?? throw new NotFoundException("Purchase requisition", id);

        if (requisition.Status == PurchaseRequisitionStatus.Cancelled)
            throw new BusinessRuleException("This requisition is already cancelled.", "ALREADY_CANCELLED");

        // Once a purchase order has drawn from it, the requisition is history and
        // has to stay resolvable from that order.
        var hasOrders = await _uow.Repository<PurchaseRequisitionDetail>()
            .AnyAsync(d => d.RequisitionId == id && d.OrderedQty > 0, ct);
        if (hasOrders)
            throw new BusinessRuleException(
                "A purchase order has already been raised from this requisition, so it cannot be cancelled.",
                "REQUISITION_USED");

        requisition.Status = PurchaseRequisitionStatus.Cancelled;
        await _uow.SaveChangesAsync(ct);

        return await GetRequisitionAsync(id, ct);
    }

    /// <summary>
    /// Applies a signed change to the OrderedQty of each requisition line and
    /// re-derives its header status. A positive delta is a PO drawing on the
    /// requisition; a negative delta is that draw being handed back (a PO edited
    /// or its lines removed). OrderedQty is clamped to [0, RequiredQty] and the
    /// header settles to Open (nothing ordered), Converted (all ordered) or
    /// Partial in between.
    /// </summary>
    private async Task AdjustRequisitionProgressAsync(
        List<(long RequisitionDetailId, decimal Delta)> changes, CancellationToken ct)
    {
        if (changes.Count == 0) return;

        var detailIds = changes.Select(c => c.RequisitionDetailId).Distinct().ToList();

        var reqIds = await _uow.Repository<PurchaseRequisitionDetail>().Query()
            .Where(d => detailIds.Contains(d.RequisitionDetailId))
            .Select(d => d.RequisitionId).Distinct().ToListAsync(ct);
        if (reqIds.Count == 0) return;

        // All lines of the affected requisitions, tracked so the OrderedQty edit
        // persists and the status is computed from the fresh in-memory values.
        var allLines = await _uow.Repository<PurchaseRequisitionDetail>().Query(tracking: true)
            .Where(d => reqIds.Contains(d.RequisitionId))
            .ToListAsync(ct);

        foreach (var (reqDetailId, delta) in changes)
        {
            var detail = allLines.FirstOrDefault(d => d.RequisitionDetailId == reqDetailId);
            if (detail is null) continue;
            detail.OrderedQty = Math.Clamp(detail.OrderedQty + delta, 0m, detail.RequiredQty);
        }

        var requisitions = await _uow.Repository<PurchaseRequisition>().Query(tracking: true)
            .Where(r => reqIds.Contains(r.RequisitionId))
            .ToListAsync(ct);

        foreach (var requisition in requisitions)
        {
            // A cancelled requisition is history and is never revived by order edits.
            if (requisition.Status == PurchaseRequisitionStatus.Cancelled) continue;

            var lines = allLines.Where(d => d.RequisitionId == requisition.RequisitionId).ToList();
            requisition.Status =
                lines.All(l => l.OrderedQty >= l.RequiredQty) ? PurchaseRequisitionStatus.Converted
                : lines.All(l => l.OrderedQty <= 0m)          ? PurchaseRequisitionStatus.Open
                :                                               PurchaseRequisitionStatus.Partial;
        }

        await _uow.SaveChangesAsync(ct);
    }

    /* ============================ helpers ============================ */

    private async Task<ItemBatch> ResolveBatchAsync(PurchaseDetail line, int locationId, CancellationToken ct)
    {
        var number = string.IsNullOrWhiteSpace(line.BatchNumber) ? "GEN" : line.BatchNumber.Trim();

        var existing = await _uow.Repository<ItemBatch>()
            .FirstOrDefaultAsync(
                b => b.ItemId == line.ItemId && b.BatchNumber == number && b.LocationId == locationId,
                tracking: true, ct);

        if (existing is not null)
        {
            existing.PurchaseRate = line.LandedRate;
            if (line.Mrp > 0) existing.Mrp = line.Mrp;
            if (line.SellingRate > 0) existing.SellingRate = line.SellingRate;
            existing.ManufacturingDate ??= line.ManufacturingDate;
            existing.ExpiryDate ??= line.ExpiryDate;
            return existing;
        }

        var batch = new ItemBatch
        {
            ItemId         = line.ItemId,
            BatchNumber       = number,
            LocationId        = locationId,
            ManufacturingDate = line.ManufacturingDate,
            ExpiryDate        = line.ExpiryDate,
            PurchaseRate      = line.LandedRate,
            Mrp               = line.Mrp,
            SellingRate       = line.SellingRate
        };

        await _uow.Repository<ItemBatch>().AddAsync(batch, ct);
        await _uow.SaveChangesAsync(ct);

        return batch;
    }

    /// <summary>
    /// Pushes the new rates onto the item master and records the change.
    /// The purchase rate becomes the landed cost, not the invoice rate, so the
    /// master reflects what the goods actually cost to have on the shelf.
    /// </summary>
    private async Task UpdateItemRatesAsync(PurchaseDetail line, string reference, CancellationToken ct)
    {
        var item = await _uow.Repository<Item>()
            .FirstOrDefaultAsync(p => p.ItemId == line.ItemId, tracking: true, ct);

        if (item is null) return;

        var oldPurchase = item.PurchaseRate;
        var oldSelling = item.SellingRate;
        var oldMrp = item.Mrp;

        item.PurchaseRate = line.LandedRate;
        if (line.Mrp > 0) item.Mrp = line.Mrp;
        if (line.SellingRate > 0) item.SellingRate = line.SellingRate;

        var changed = oldPurchase != item.PurchaseRate
                      || oldSelling != item.SellingRate
                      || oldMrp != item.Mrp;

        if (!changed) return;

        await _uow.Repository<ItemPriceHistory>().AddAsync(new ItemPriceHistory
        {
            ItemId       = item.ItemId,
            ChangeSource    = PriceChangeSource.Purchase,
            ReferenceNumber = reference,
            OldPurchaseRate = oldPurchase,
            NewPurchaseRate = item.PurchaseRate,
            OldSellingRate  = oldSelling,
            NewSellingRate  = item.SellingRate,
            OldMrp          = oldMrp,
            NewMrp          = item.Mrp
        }, ct);
    }

    private async Task UpdatePurchaseOrderProgressAsync(
        Purchase purchase, List<PurchaseDetail> lines, CancellationToken ct)
    {
        if (purchase.PurchaseOrderId is not { } orderId) return;

        var orderLines = await _uow.Repository<PurchaseOrderDetail>().Query(tracking: true)
            .Where(d => d.PurchaseOrderId == orderId)
            .ToListAsync(ct);

        foreach (var orderLine in orderLines)
        {
            var received = lines.Where(l => l.ItemId == orderLine.ItemId).Sum(l => l.Quantity);
            if (received <= 0) continue;

            // Capped at the ordered quantity: CK_PurchaseOrderDetails_Qty
            // rejects over-receipt, and an over-delivery is a conversation with
            // the supplier rather than a reason to fail the posting.
            orderLine.ReceivedQty = Math.Min(orderLine.OrderedQty, orderLine.ReceivedQty + received);
        }

        var order = await _uow.Repository<PurchaseOrder>()
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == orderId, tracking: true, ct);

        if (order is null) return;

        order.Status = orderLines.All(l => l.ReceivedQty >= l.OrderedQty)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.Partial;
    }

    private async Task GuardDuplicateSupplierBillAsync(
        int supplierId, string? supplierInvoiceNumber, long? purchaseId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(supplierInvoiceNumber)) return;

        var number = supplierInvoiceNumber.Trim();

        // Mirrors UQ_Purchases_Supplier_InvoiceNumber. Entering the same
        // supplier bill twice is the most common and most expensive data-entry
        // error in a purchase module: it doubles both stock and the payable.
        var exists = await _uow.Repository<Purchase>().AnyAsync(
            p => p.SupplierId == supplierId
                 && p.SupplierInvoiceNumber == number
                 && p.Status != DocumentStatus.Cancelled
                 && p.PurchaseId != purchaseId, ct);

        if (exists)
            throw new ConflictException(
                $"Supplier bill '{number}' has already been entered for this supplier.");
    }

    private async Task GuardWarehouseAsync(int? warehouseId, CancellationToken ct)
    {
        if (warehouseId is { } id && !await _uow.Repository<WarehouseMaster>()
                .AnyAsync(w => w.WarehouseId == id && !w.IsDeleted, ct))
            throw new ValidationException("WarehouseId", "The selected warehouse does not exist.");
    }

    private async Task<bool> IsInterStateAsync(int? supplierStateId, CancellationToken ct)
    {
        var shopStateId = await _uow.Repository<CompanyProfile>().Query()
            .Select(c => c.StateId)
            .FirstOrDefaultAsync(ct);

        // With either state unknown we assume intra-state: CGST+SGST is the
        // overwhelmingly common case for a village shop, and guessing IGST
        // would put the wrong tax heads on a real invoice.
        if (shopStateId is null || supplierStateId is null) return false;

        return shopStateId != supplierStateId;
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
