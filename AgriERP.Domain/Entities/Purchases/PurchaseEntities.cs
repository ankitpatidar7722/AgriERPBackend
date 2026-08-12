using AgriERP.Domain.Common;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.Purchases;

/// <summary>
/// Maps to PurchaseRequisitions - the "what to buy" document that precedes a
/// Purchase Order. It carries no supplier; a PO is raised from it, and pulling a
/// line adds to its OrderedQty. Open -> Partial -> Converted follows PendingQty.
/// </summary>
public class PurchaseRequisition : DocumentEntity
{
    public long RequisitionId { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateTime RequisitionDate { get; set; }
    public int LocationId { get; set; }
    public string? Remarks { get; set; }

    public decimal TotalQty { get; set; }

    public PurchaseRequisitionStatus Status { get; set; } = PurchaseRequisitionStatus.Open;

    /// <summary>The requisition voucher (REQ). See VoucherMaster.</summary>
    public int? VoucherId { get; set; }

    public StorageLocation? Location { get; set; }
    public VoucherMaster? Voucher { get; set; }
    public ICollection<PurchaseRequisitionDetail> Details { get; set; } = new List<PurchaseRequisitionDetail>();
}

/// <summary>Maps to PurchaseRequisitionDetails.</summary>
public class PurchaseRequisitionDetail
{
    public long RequisitionDetailId { get; set; }
    public long RequisitionId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public decimal RequiredQty { get; set; }

    /// <summary>Maintained as purchase orders consume this line.</summary>
    public decimal OrderedQty { get; set; }

    /// <summary>PERSISTED computed column: RequiredQty - OrderedQty.</summary>
    public decimal PendingQty { get; private set; }

    public int UnitId { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public decimal EstimatedRate { get; set; }
    public string? Remarks { get; set; }

    public PurchaseRequisition? Requisition { get; set; }
    public Item? Item { get; set; }
    public Unit? Unit { get; set; }
}

/// <summary>Maps to PurchaseOrders.</summary>
public class PurchaseOrder : DocumentEntity
{
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public int SupplierId { get; set; }
    public int LocationId { get; set; }
    public string? Remarks { get; set; }

    public decimal TotalQty { get; set; }
    public decimal EstimatedValue { get; set; }

    /// <summary>Open -> Partial -> Received. Drives the pending-purchase screen.</summary>
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    /// <summary>Which document type this row is - the PO voucher. See VoucherMaster.</summary>
    public int? VoucherId { get; set; }

    public Supplier? Supplier { get; set; }
    public StorageLocation? Location { get; set; }
    public VoucherMaster? Voucher { get; set; }
    public ICollection<PurchaseOrderDetail> Details { get; set; } = new List<PurchaseOrderDetail>();
}

/// <summary>Maps to PurchaseOrderDetails.</summary>
public class PurchaseOrderDetail
{
    public long PurchaseOrderDetailId { get; set; }
    public long PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public decimal OrderedQty { get; set; }

    /// <summary>Maintained as goods arrive; the PO status is derived from this.</summary>
    public decimal ReceivedQty { get; set; }

    /// <summary>PERSISTED computed column: OrderedQty - ReceivedQty.</summary>
    public decimal PendingQty { get; private set; }

    public int UnitId { get; set; }
    public decimal Rate { get; set; }

    /// <summary>PERSISTED computed column: OrderedQty * Rate.</summary>
    public decimal EstimatedAmount { get; private set; }

    /// <summary>Packs ordered. OrderedQty (the P.O. qty in purchase units) = NoOfPacks * QtyPerPack.</summary>
    public decimal NoOfPacks { get; set; }

    /// <summary>Units contained in one pack.</summary>
    public decimal QtyPerPack { get; set; }

    /// <summary>Snapshot of the requisition line's required qty; null for a direct order.</summary>
    public decimal? RequiredQty { get; set; }

    public string? Remarks { get; set; }

    /// <summary>A note about the item on this order, kept apart from Remarks.</summary>
    public string? ItemRemark { get; set; }

    /// <summary>The requisition line this order line fulfils, when raised from one.</summary>
    public long? RequisitionDetailId { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
    public Item? Item { get; set; }
    public Unit? Unit { get; set; }
    public PurchaseRequisitionDetail? RequisitionDetail { get; set; }
}

/// <summary>
/// Maps to Purchases (PurchaseMaster).
///
/// GrandTotal, BalanceAmount and PaymentStatus are PERSISTED computed columns.
/// The API writes the components; it never writes a total. A total that
/// disagrees with its own parts is therefore not expressible.
/// </summary>
public class Purchase : DocumentEntity
{
    public long PurchaseId { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public int SupplierId { get; set; }

    /// <summary>
    /// The supplier's own bill number. Needed for GSTR-2 reconciliation, and
    /// unique per supplier so the same bill cannot be entered twice.
    /// </summary>
    public string? SupplierInvoiceNumber { get; set; }

    public DateTime? SupplierInvoiceDate { get; set; }
    public long? PurchaseOrderId { get; set; }
    public int LocationId { get; set; }

    /// <summary>The warehouse the goods were received into (WarehouseMaster). A label
    /// shown in place of "location"; stock still posts to LocationId. Optional.</summary>
    public int? WarehouseId { get; set; }

    /// <summary>Decides CGST+SGST versus IGST. Enforced by CK_Purchases_TaxMode.</summary>
    public bool IsInterState { get; set; }

    public int? SupplierStateId { get; set; }

    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal CessAmount { get; set; }
    public decimal FreightCharges { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal RoundOff { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal GrandTotal { get; private set; }

    public decimal PaidAmount { get; set; }

    /// <summary>PERSISTED computed column: GrandTotal - PaidAmount.</summary>
    public decimal BalanceAmount { get; private set; }

    /// <summary>PERSISTED computed column derived from PaidAmount vs GrandTotal.</summary>
    public PaymentSettlementStatus PaymentStatus { get; private set; }

    public DateTime? DueDate { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    public string? Remarks { get; set; }

    /// <summary>
    /// Which document type this row is. Under Option B every purchase row is a
    /// GRN (goods received, stock in); the PO lives in PurchaseOrder. See VoucherMaster.
    /// </summary>
    public int? VoucherId { get; set; }

    public Supplier? Supplier { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public StorageLocation? Location { get; set; }
    public WarehouseMaster? Warehouse { get; set; }
    public State? SupplierState { get; set; }
    public VoucherMaster? Voucher { get; set; }
    public ICollection<PurchaseDetail> Details { get; set; } = new List<PurchaseDetail>();
}

/// <summary>Maps to PurchaseDetails.</summary>
public class PurchaseDetail
{
    public long PurchaseDetailId { get; set; }
    public long PurchaseId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }

    /// <summary>Resolved or created when the purchase is posted.</summary>
    public long? BatchId { get; set; }

    // Batch data as keyed in, kept on the line as well as on the batch row so
    // the printed bill still reproduces what was entered even if the batch
    // master is later corrected.
    public string? BatchNumber { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// Scheme goods ("10 + 1 free"). Adds to stock, adds nothing to cost -
    /// which is exactly why it lowers the effective purchase rate.
    /// </summary>
    public decimal FreeQuantity { get; set; }

    /// <summary>PERSISTED computed column: Quantity + FreeQuantity.</summary>
    public decimal TotalQuantity { get; private set; }

    public int UnitId { get; set; }
    public decimal Rate { get; set; }

    /// <summary>PERSISTED computed column: Quantity * Rate.</summary>
    public decimal GrossAmount { get; private set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>PERSISTED computed column: GrossAmount - DiscountAmount.</summary>
    public decimal TaxableAmount { get; private set; }

    public decimal GstPercent { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal CessAmount { get; set; }

    /// <summary>PERSISTED computed column: TaxableAmount + all taxes.</summary>
    public decimal LineTotal { get; private set; }

    /// <summary>
    /// Landed cost per unit including freight share and free goods. A sale
    /// copies this as its cost, so reported profit is real rather than notional.
    /// </summary>
    public decimal LandedRate { get; set; }

    /// <summary>New MRP arriving with this consignment; posting pushes it onto the batch.</summary>
    public decimal Mrp { get; set; }

    public decimal SellingRate { get; set; }
    public string? HsnCode { get; set; }
    public string? Remarks { get; set; }

    public Purchase? Purchase { get; set; }
    public Item? Item { get; set; }
    public ItemBatch? Batch { get; set; }
    public Unit? Unit { get; set; }
}

/// <summary>Maps to PurchaseReturns.</summary>
public class PurchaseReturn : DocumentEntity
{
    public long PurchaseReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int SupplierId { get; set; }

    /// <summary>Optional: expired stock is often returned without citing a bill.</summary>
    public long? PurchaseId { get; set; }

    public int LocationId { get; set; }
    public string? DebitNoteNumber { get; set; }
    public string? ReturnReason { get; set; }

    public bool IsInterState { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal CessAmount { get; set; }
    public decimal RoundOff { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal GrandTotal { get; private set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }

    public Supplier? Supplier { get; set; }
    public Purchase? Purchase { get; set; }
    public StorageLocation? Location { get; set; }
    public ICollection<PurchaseReturnDetail> Details { get; set; } = new List<PurchaseReturnDetail>();
}

/// <summary>Maps to PurchaseReturnDetails.</summary>
public class PurchaseReturnDetail
{
    public long PurchaseReturnDetailId { get; set; }
    public long PurchaseReturnId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public long BatchId { get; set; }

    /// <summary>Original purchase line, when the return cites a bill.</summary>
    public long? PurchaseDetailId { get; set; }

    public decimal Quantity { get; set; }
    public int UnitId { get; set; }
    public decimal Rate { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal GrossAmount { get; private set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal TaxableAmount { get; private set; }

    public decimal GstPercent { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal CessAmount { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal LineTotal { get; private set; }

    public string? ReturnReason { get; set; }

    public PurchaseReturn? PurchaseReturn { get; set; }
    public Item? Item { get; set; }
    public ItemBatch? Batch { get; set; }
    public PurchaseDetail? PurchaseDetail { get; set; }
    public Unit? Unit { get; set; }
}
