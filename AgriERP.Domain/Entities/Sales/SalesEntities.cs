using AgriERP.Domain.Common;
using AgriERP.Domain.Entities.Finance;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Entities.Security;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.Sales;

/// <summary>
/// Maps to Sales (SalesMaster).
///
/// SaleType and PaymentType are separate axes on purpose: a wholesale sale on
/// credit is an everyday transaction and one column cannot express it.
/// CK_Sales_CreditNeedsCustomer enforces the rule that matters - credit needs
/// a named customer, never a walk-in.
/// </summary>
public class Sale : DocumentEntity
{
    public long SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }

    /// <summary>Counter-rush analysis; the invoice date alone loses the time of day.</summary>
    public TimeSpan? InvoiceTime { get; set; }

    /// <summary>Null for a pure walk-in cash sale.</summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// Name and mobile taken at the counter for a one-time buyer, without
    /// polluting the customer master with hundreds of throwaway records.
    /// </summary>
    public string? WalkInCustomerName { get; set; }
    public string? WalkInMobile { get; set; }

    /// <summary>Which price list applied.</summary>
    public SaleType SaleType { get; set; } = SaleType.Retail;

    /// <summary>How it was settled.</summary>
    public SalePaymentType PaymentType { get; set; } = SalePaymentType.Cash;

    public int LocationId { get; set; }

    /// <summary>Users - drives salesman-wise reports and incentives.</summary>
    public int? SalesmanId { get; set; }

    public bool IsInterState { get; set; }
    public int? PlaceOfSupplyStateId { get; set; }

    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal CessAmount { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal RoundOff { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal GrandTotal { get; private set; }

    /// <summary>Cost of goods on this invoice, summed from the lines.</summary>
    public decimal TotalCostAmount { get; set; }

    /// <summary>PERSISTED computed column: TaxableAmount - TotalCostAmount.</summary>
    public decimal GrossProfit { get; private set; }

    public decimal ReceivedAmount { get; set; }

    /// <summary>PERSISTED computed column: GrandTotal - ReceivedAmount.</summary>
    public decimal BalanceAmount { get; private set; }

    /// <summary>PERSISTED computed column.</summary>
    public PaymentSettlementStatus PaymentStatus { get; private set; }

    public DateTime? DueDate { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    public int PrintCount { get; set; }
    public string? Remarks { get; set; }

    public Customer? Customer { get; set; }
    public StorageLocation? Location { get; set; }
    public User? Salesman { get; set; }
    public State? PlaceOfSupplyState { get; set; }

    public ICollection<SalesDetail> Details { get; set; } = new List<SalesDetail>();
    public ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();

    /// <summary>Display name for the bill: registered customer, walk-in name, or cash.</summary>
    public string DisplayCustomerName =>
        Customer?.CustomerName ?? WalkInCustomerName ?? "Cash Customer";
}

/// <summary>Maps to SalesDetails.</summary>
public class SalesDetail
{
    public long SalesDetailId { get; set; }
    public long SaleId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }

    /// <summary>
    /// Which physical lot left the shelf. Mandatory: without it there is no
    /// expiry traceability and no honest cost.
    /// </summary>
    public long BatchId { get; set; }

    public string? BatchNumber { get; set; }    // printed on the bill
    public DateTime? ExpiryDate { get; set; }   // printed on the bill

    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal TotalQuantity { get; private set; }

    public int UnitId { get; set; }
    public decimal Mrp { get; set; }
    public decimal Rate { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal GrossAmount { get; private set; }

    public decimal DiscountPercent { get; set; }
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

    /// <summary>
    /// Frozen cost of the batch sold, copied at the moment of sale. Deriving
    /// profit later from the item's current purchase rate would silently
    /// restate last year's profit each time a new consignment arrives.
    /// </summary>
    public decimal CostRate { get; set; }

    /// <summary>
    /// PERSISTED computed column charged on Quantity + FreeQuantity: free goods
    /// carry cost but earn no revenue.
    /// </summary>
    public decimal CostAmount { get; private set; }

    /// <summary>PERSISTED computed column: TaxableAmount - CostAmount.</summary>
    public decimal LineProfit { get; private set; }

    public string? HsnCode { get; set; }
    public string? Remarks { get; set; }

    public Sale? Sale { get; set; }
    public Item? Item { get; set; }
    public ItemBatch? Batch { get; set; }
    public Unit? Unit { get; set; }
}

/// <summary>
/// Maps to SalePayments - the split when one bill is settled part cash,
/// part UPI. One row per tender.
/// </summary>
public class SalePayment
{
    public long SalePaymentId { get; set; }
    public long SaleId { get; set; }
    public int PaymentModeId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>UPI reference, cheque number or card authorisation code.</summary>
    public string? ReferenceNumber { get; set; }

    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }

    public Sale? Sale { get; set; }
    public PaymentMode? PaymentMode { get; set; }
}

/// <summary>Maps to SalesReturns.</summary>
public class SalesReturn : DocumentEntity
{
    public long SalesReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int? CustomerId { get; set; }

    /// <summary>Original invoice, when the farmer produces it.</summary>
    public long? SaleId { get; set; }

    public int LocationId { get; set; }
    public string? CreditNoteNumber { get; set; }
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

    public decimal TotalCostAmount { get; set; }

    /// <summary>Cash back over the counter, or knocked off the customer's ledger.</summary>
    public SalesReturnRefundMode RefundMode { get; set; } = SalesReturnRefundMode.Adjust;

    public decimal RefundedAmount { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public string? Remarks { get; set; }

    public Customer? Customer { get; set; }
    public Sale? Sale { get; set; }
    public StorageLocation? Location { get; set; }
    public ICollection<SalesReturnDetail> Details { get; set; } = new List<SalesReturnDetail>();
}

/// <summary>Maps to SalesReturnDetails.</summary>
public class SalesReturnDetail
{
    public long SalesReturnDetailId { get; set; }
    public long SalesReturnId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public long BatchId { get; set; }

    /// <summary>Original sale line, when the return cites an invoice.</summary>
    public long? SalesDetailId { get; set; }

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

    /// <summary>
    /// The same cost the goods left at, so a sale-then-return pair nets to zero
    /// profit rather than a phantom gain.
    /// </summary>
    public decimal CostRate { get; set; }

    /// <summary>PERSISTED computed column.</summary>
    public decimal CostAmount { get; private set; }

    /// <summary>Expired or damaged goods come back but must not go back on the shelf.</summary>
    public bool IsSaleable { get; set; } = true;

    public string? ReturnReason { get; set; }

    public SalesReturn? SalesReturn { get; set; }
    public Item? Item { get; set; }
    public ItemBatch? Batch { get; set; }
    public SalesDetail? SalesDetail { get; set; }
    public Unit? Unit { get; set; }
}
