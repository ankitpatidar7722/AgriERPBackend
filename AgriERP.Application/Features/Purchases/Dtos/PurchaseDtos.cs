using AgriERP.Application.Features.Sales.Dtos;
using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Purchases.Dtos;

public class PurchaseLineRequest
{
    public int ItemId { get; set; }

    /// <summary>
    /// The PO line this receipt fills, when receiving against pending orders.
    /// Null for a direct stock-in. Drives per-line PO reconciliation, so one GRN
    /// can span several purchase orders of the same supplier.
    /// </summary>
    public long? PurchaseOrderDetailId { get; set; }

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

    /// <summary>The PO line this receipt filled, so editing a draft keeps the link.</summary>
    public long? PurchaseOrderDetailId { get; set; }

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

    /// <summary>The P.O. quantity in purchase units - the client sends NoOfPacks * QtyPerPack.</summary>
    public decimal OrderedQty { get; set; }
    public int? UnitId { get; set; }
    public decimal Rate { get; set; }

    /// <summary>Pack breakdown that produced OrderedQty.</summary>
    public decimal NoOfPacks { get; set; }
    public decimal QtyPerPack { get; set; }

    /// <summary>Snapshot of the requisition's required qty; null for a direct order.</summary>
    public decimal? RequiredQty { get; set; }

    public string? Remarks { get; set; }

    /// <summary>A per-line note about the item, distinct from Remarks.</summary>
    public string? ItemRemark { get; set; }

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
    public string ItemCode { get; set; } = string.Empty;
    public string ItemGroupName { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal PendingQty { get; set; }
    public decimal Rate { get; set; }
    public decimal EstimatedAmount { get; set; }

    // Pack breakdown and the requisition figure this line was built against.
    public decimal NoOfPacks { get; set; }
    public decimal QtyPerPack { get; set; }
    public decimal? RequiredQty { get; set; }
    public string? Remarks { get; set; }
    public string? ItemRemark { get; set; }

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

/// <summary>
/// One purchase-order LINE flattened with its parent order's header fields, for
/// the item-wise order list: every item is its own row (a 5-item order shows as
/// 5 rows), and each row carries its PurchaseOrderId so it can open/print/receive
/// against the whole order.
/// </summary>
public class PurchaseOrderItemRowDto
{
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public PurchaseOrderStatus Status { get; set; }

    public long PurchaseOrderDetailId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemGroupName { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal Rate { get; set; }
    public decimal EstimatedAmount { get; set; }
}

/// <summary>
/// One GRN (purchase) LINE flattened with its parent's header, for the item-wise
/// GRN list: every item is its own row, each carrying its PurchaseId.
/// </summary>
public class PurchaseItemRowDto
{
    public long PurchaseId { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? WarehouseName { get; set; }
    public DocumentStatus Status { get; set; }

    public long PurchaseDetailId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemGroupName { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>Supplier identity shown on the printed purchase order.</summary>
public class PurchaseOrderSupplierDto
{
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? StateName { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    public int PaymentTermDays { get; set; }
}

/// <summary>
/// Everything the printable purchase order needs in one payload: the shop
/// letterhead (sourced from ShopMaster, falling back to CompanyProfile),
/// the supplier block, the order with its lines, and estimated totals. The
/// GST here is an ESTIMATE off each line's slab - the binding tax is struck
/// on the goods-receipt note when the consignment arrives.
/// </summary>
public class PurchaseOrderPrintDto
{
    public ShopHeaderDto Shop { get; set; } = new();
    public PurchaseOrderDto Order { get; set; } = new();
    public PurchaseOrderSupplierDto Supplier { get; set; } = new();
    public string? DeliveryLocationName { get; set; }
    public bool IsInterState { get; set; }
    public IReadOnlyList<InvoiceTaxSummaryDto> TaxSummary { get; set; } = Array.Empty<InvoiceTaxSummaryDto>();
    public int TotalPacks { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public string AmountInWords { get; set; } = string.Empty;
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
