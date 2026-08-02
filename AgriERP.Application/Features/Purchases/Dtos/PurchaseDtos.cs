using AgriERP.Application.Features.Sales.Dtos;
using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Purchases.Dtos;

public class PurchaseLineRequest
{
    public int ItemId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Scheme goods ("10 + 1 free"). Adds to stock, adds nothing to cost.</summary>
    public decimal FreeQuantity { get; set; }

    public decimal Rate { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>New MRP and selling rate arriving with this consignment.</summary>
    public decimal Mrp { get; set; }
    public decimal SellingRate { get; set; }

    public string? Remarks { get; set; }
}

public class SavePurchaseRequest
{
    public DateTime PurchaseDate { get; set; }
    public int SupplierId { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public long? PurchaseOrderId { get; set; }
    public int? LocationId { get; set; }
    public int? WarehouseId { get; set; }

    public decimal FreightCharges { get; set; }
    public decimal OtherCharges { get; set; }

    /// <summary>Paid at the time of entry. The rest becomes supplier credit.</summary>
    public decimal PaidAmount { get; set; }

    public string? Remarks { get; set; }

    /// <summary>
    /// Days until the supplier balance is due, set on the bill. When null the
    /// supplier's own payment term is used. Drives DueDate.
    /// </summary>
    public int? CreditDays { get; set; }

    public List<PurchaseLineRequest> Lines { get; set; } = new();
}

public class PurchaseLineDto
{
    public long PurchaseDetailId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public long? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Cost per unit after freight share and free goods. Sales copy this.</summary>
    public decimal LandedRate { get; set; }

    public decimal Mrp { get; set; }
    public decimal SellingRate { get; set; }
    public string? HsnCode { get; set; }
}

public class PurchaseListDto
{
    public long PurchaseId { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public DocumentStatus Status { get; set; }
    public DateTime? DueDate { get; set; }
    public int LineCount { get; set; }
    public string? WarehouseName { get; set; }
}

public class PurchaseDto : PurchaseListDto
{
    public DateTime? SupplierInvoiceDate { get; set; }
    public long? PurchaseOrderId { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int? WarehouseId { get; set; }
    public bool IsInterState { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal FreightCharges { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal RoundOff { get; set; }
    public string? Remarks { get; set; }
    public DateTime? PostedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<PurchaseLineDto> Lines { get; set; } = Array.Empty<PurchaseLineDto>();
}

/// <summary>Everything the printable purchase order needs: shop, bill, tax summary, words.</summary>
public class PurchasePrintDto
{
    public ShopHeaderDto Shop { get; set; } = new();
    public PurchaseDto Purchase { get; set; } = new();
    public IReadOnlyList<InvoiceTaxSummaryDto> TaxSummary { get; set; } = Array.Empty<InvoiceTaxSummaryDto>();
    public string AmountInWords { get; set; } = string.Empty;
}

public class PurchaseQueryParameters : QueryParameters
{
    public int? SupplierId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DocumentStatus? Status { get; set; }

    /// <summary>True returns only bills with money still owed.</summary>
    public bool? UnpaidOnly { get; set; }
}

/* ---------------------------- purchase return ---------------------------- */

public class PurchaseReturnLineRequest
{
    public long BatchId { get; set; }
    public long? PurchaseDetailId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? ReturnReason { get; set; }
}

public class SavePurchaseReturnRequest
{
    public DateTime ReturnDate { get; set; }
    public int SupplierId { get; set; }
    public long? PurchaseId { get; set; }
    public int? LocationId { get; set; }
    public string? DebitNoteNumber { get; set; }
    public string? ReturnReason { get; set; }
    public string? Remarks { get; set; }
    public List<PurchaseReturnLineRequest> Lines { get; set; } = new();
}

public class PurchaseReturnLineDto
{
    public long PurchaseReturnDetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public long BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal LineTotal { get; set; }
    public string? ReturnReason { get; set; }
}

public class PurchaseReturnDto
{
    public long PurchaseReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long? PurchaseId { get; set; }
    public string? DebitNoteNumber { get; set; }
    public string? ReturnReason { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public DocumentStatus Status { get; set; }
    public DateTime? PostedAt { get; set; }
    public IReadOnlyList<PurchaseReturnLineDto> Lines { get; set; } = Array.Empty<PurchaseReturnLineDto>();
}

public class PurchaseReturnQueryParameters : QueryParameters
{
    public int? SupplierId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DocumentStatus? Status { get; set; }
}

/* ---------------------------- purchase order ---------------------------- */

public class PurchaseOrderLineRequest
{
    public int ItemId { get; set; }
    public decimal OrderedQty { get; set; }
    public int? UnitId { get; set; }
    public decimal Rate { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Set when this PO line is raised from a requisition line - it adds to that line's OrderedQty.</summary>
    public long? RequisitionDetailId { get; set; }
}

public class SavePurchaseOrderRequest
{
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public int SupplierId { get; set; }
    public int? LocationId { get; set; }
    public string? Remarks { get; set; }
    public List<PurchaseOrderLineRequest> Lines { get; set; } = new();
}

public class PurchaseOrderLineDto
{
    public long PurchaseOrderDetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal PendingQty { get; set; }
    public decimal Rate { get; set; }
    public decimal EstimatedAmount { get; set; }

    // The requisition line this order line drew on, if any. Round-tripped so an
    // edit can hand the draw back and re-take it, keeping requisition progress true.
    public long? RequisitionDetailId { get; set; }

    // Carried from the item master so a goods-receipt raised against this order
    // can open each line with the right tax and prices already in place.
    public decimal GstPercent { get; set; }
    public string? HsnCode { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingRate { get; set; }
}

public class PurchaseOrderDto
{
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal TotalQty { get; set; }
    public decimal EstimatedValue { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public string? Remarks { get; set; }
    public IReadOnlyList<PurchaseOrderLineDto> Lines { get; set; } = Array.Empty<PurchaseOrderLineDto>();
}

public class PurchaseOrderQueryParameters : QueryParameters
{
    public int? SupplierId { get; set; }
    public PurchaseOrderStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>True returns orders still awaiting goods.</summary>
    public bool? PendingOnly { get; set; }
}
