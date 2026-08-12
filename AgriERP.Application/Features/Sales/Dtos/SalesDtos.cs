using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Sales.Dtos;

public class SaleLineRequest
{
    public int ItemId { get; set; }

    /// <summary>
    /// Leave null to let the server pick batches by FEFO. One request line may
    /// then become several invoice lines when the quantity spans batches.
    /// </summary>
    public long? BatchId { get; set; }

    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }

    /// <summary>Null uses the rate for the customer's price type.</summary>
    public decimal? Rate { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Remarks { get; set; }
}

public class SalePaymentRequest
{
    public int PaymentModeId { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }
}

public class SaveSaleRequest
{
    public DateTime InvoiceDate { get; set; }

    /// <summary>Null for a walk-in cash sale. Required for credit.</summary>
    public int? CustomerId { get; set; }

    public string? WalkInCustomerName { get; set; }
    public string? WalkInMobile { get; set; }

    public SaleType SaleType { get; set; } = SaleType.Retail;
    public SalePaymentType PaymentType { get; set; } = SalePaymentType.Cash;
    public int? LocationId { get; set; }
    public int? SalesmanId { get; set; }

    public decimal OtherCharges { get; set; }
    public string? Remarks { get; set; }

    /// <summary>
    /// Days until a credit balance is due, set on the bill. When null the
    /// customer's own default term is used. Drives DueDate.
    /// </summary>
    public int? CreditDays { get; set; }

    public List<SaleLineRequest> Lines { get; set; } = new();

    /// <summary>Cash / UPI / card split. Their total becomes the received amount.</summary>
    public List<SalePaymentRequest> Payments { get; set; } = new();
}

public class SaleLineDto
{
    public long SalesDetailId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public long BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal Mrp { get; set; }
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
    public string? HsnCode { get; set; }

    /// <summary>Only returned to users holding Report.Profit.</summary>
    public decimal? CostRate { get; set; }
    public decimal? LineProfit { get; set; }
}

public class SalePaymentDto
{
    public long SalePaymentId { get; set; }
    public int PaymentModeId { get; set; }
    public string PaymentModeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }
}

public class SaleListDto
{
    public long SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Village { get; set; }
    public SaleType SaleType { get; set; }
    public SalePaymentType PaymentType { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public DocumentStatus Status { get; set; }
    public int LineCount { get; set; }
}

public class SaleDto : SaleListDto
{
    public string? WalkInCustomerName { get; set; }
    public string? WalkInMobile { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int? SalesmanId { get; set; }
    public string? SalesmanName { get; set; }
    public bool IsInterState { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal RoundOff { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Remarks { get; set; }
    public DateTime? PostedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public int PrintCount { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Only populated for users holding Report.Profit.</summary>
    public decimal? TotalCostAmount { get; set; }
    public decimal? GrossProfit { get; set; }

    public IReadOnlyList<SaleLineDto> Lines { get; set; } = Array.Empty<SaleLineDto>();
    public IReadOnlyList<SalePaymentDto> Payments { get; set; } = Array.Empty<SalePaymentDto>();
}

public class SaleQueryParameters : QueryParameters
{
    public int? CustomerId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DocumentStatus? Status { get; set; }
    public SaleType? SaleType { get; set; }
    public SalePaymentType? PaymentType { get; set; }
    public int? SalesmanId { get; set; }

    /// <summary>True returns only invoices with money still outstanding.</summary>
    public bool? UnpaidOnly { get; set; }
}

/* ---------------------------- sales return ---------------------------- */

public class SalesReturnLineRequest
{
    public long BatchId { get; set; }
    public long? SalesDetailId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>False for expired or damaged goods - they come back but never reach the shelf.</summary>
    public bool IsSaleable { get; set; } = true;

    public string? ReturnReason { get; set; }
}

public class SaveSalesReturnRequest
{
    public DateTime ReturnDate { get; set; }
    public int? CustomerId { get; set; }
    public long? SaleId { get; set; }
    public int? LocationId { get; set; }
    public string? ReturnReason { get; set; }
    public SalesReturnRefundMode RefundMode { get; set; } = SalesReturnRefundMode.Adjust;
    public decimal RefundedAmount { get; set; }
    public string? Remarks { get; set; }
    public List<SalesReturnLineRequest> Lines { get; set; } = new();
}

public class SalesReturnLineDto
{
    public long SalesReturnDetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public long BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal LineTotal { get; set; }
    public bool IsSaleable { get; set; }
    public string? ReturnReason { get; set; }
}

public class SalesReturnDto
{
    public long SalesReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public long? SaleId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? CreditNoteNumber { get; set; }
    public string? ReturnReason { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public SalesReturnRefundMode RefundMode { get; set; }
    public decimal RefundedAmount { get; set; }
    public DocumentStatus Status { get; set; }
    public DateTime? PostedAt { get; set; }
    public IReadOnlyList<SalesReturnLineDto> Lines { get; set; } = Array.Empty<SalesReturnLineDto>();
}

public class SalesReturnQueryParameters : QueryParameters
{
    public int? CustomerId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DocumentStatus? Status { get; set; }
}

/* ---------------------------- invoice print ---------------------------- */

public class InvoicePrintDto
{
    public ShopHeaderDto Shop { get; set; } = new();
    public SaleDto Invoice { get; set; } = new();

    /// <summary>Rate-wise tax summary, as an Indian tax invoice must show.</summary>
    public IReadOnlyList<InvoiceTaxSummaryDto> TaxSummary { get; set; } = Array.Empty<InvoiceTaxSummaryDto>();

    public string AmountInWords { get; set; } = string.Empty;
}

/* --------------------------- sales order print -------------------------- */

/// <summary>Customer identity shown on the printed sales order.</summary>
public class SalesOrderCustomerDto
{
    public string Name { get; set; } = string.Empty;
    public string? Village { get; set; }
    public string? Mobile { get; set; }
    public string? GstNumber { get; set; }
}

/// <summary>
/// The sales-order document: the shop letterhead (ShopMaster first), the
/// customer block, the sale with its lines, its real GST and the amount in
/// words. Unlike the tax invoice this does not bump the print count - a sales
/// order is an order confirmation, not a fiscal reprint.
/// </summary>
public class SalesOrderPrintDto
{
    public ShopHeaderDto Shop { get; set; } = new();
    public SaleDto Sale { get; set; } = new();
    public SalesOrderCustomerDto Customer { get; set; } = new();
    public IReadOnlyList<InvoiceTaxSummaryDto> TaxSummary { get; set; } = Array.Empty<InvoiceTaxSummaryDto>();
    public string AmountInWords { get; set; } = string.Empty;
}

public class ShopHeaderDto
{
    public string ShopName { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? StateName { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PesticideLicenceNo { get; set; }
    public string? SeedLicenceNo { get; set; }
    public string? FertilizerLicenceNo { get; set; }
    public string? InvoiceTerms { get; set; }
    public string? InvoiceFooterNote { get; set; }
    public string? UpiId { get; set; }
    public string? LogoPath { get; set; }
}

public class InvoiceTaxSummaryDto
{
    public string? HsnCode { get; set; }
    public decimal GstPercent { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalTax { get; set; }
}
