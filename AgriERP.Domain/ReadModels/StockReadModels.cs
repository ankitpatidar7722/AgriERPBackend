namespace AgriERP.Domain.ReadModels;

/*
 * Keyless read models mapped to the reporting views in 10_Views.sql.
 *
 * They live in Domain rather than Persistence because the Application layer
 * builds DTOs from them, and Application cannot reference Persistence without
 * inverting the Clean Architecture dependency direction.
 *
 * All are read-only. EF Core is told so via HasNoKey().ToView(...), so no
 * amount of accidental SaveChanges can attempt to write one.
 */

/// <summary>vw_ItemStock - current stock per item, rolled up from batches.</summary>
public class ItemStockView
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public int ItemSubGroupId { get; set; }
    public string ItemSubGroupName { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal MinStockLevel { get; set; }
    public decimal MaxStockLevel { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SellingRate { get; set; }
    public decimal Mrp { get; set; }
    public string? RackNumber { get; set; }
    public bool IsActive { get; set; }

    public decimal CurrentStock { get; set; }
    public long BatchCount { get; set; }

    /// <summary>Valued at each batch's own landed cost, not the item's current rate.</summary>
    public decimal StockValueAtCost { get; set; }

    public decimal StockValueAtMrp { get; set; }
    public DateTime? NearestExpiryDate { get; set; }

    /// <summary>OutOfStock | LowStock | OverStock | Normal.</summary>
    public string StockStatus { get; set; } = string.Empty;
}

/// <summary>vw_BatchStock - batch-wise stock with expiry classification.</summary>
public class BatchStockView
{
    public long BatchId { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int ItemSubGroupId { get; set; }
    public string ItemSubGroupName { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SellingRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal InwardQty { get; set; }
    public decimal OutwardQty { get; set; }
    public decimal CurrentQty { get; set; }
    public decimal StockValueAtCost { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public int? DaysToExpiry { get; set; }

    /// <summary>NoExpiry | Expired | Critical (30d) | Warning (90d) | Safe.</summary>
    public string ExpiryStatus { get; set; } = string.Empty;
}

/// <summary>vw_StockLedger - the movement journal with a running balance.</summary>
public class StockLedgerView
{
    public long StockTransactionId { get; set; }
    public DateTime TransactionDate { get; set; }
    public byte TransactionTypeId { get; set; }
    public string TransactionTypeCode { get; set; } = string.Empty;
    public string TransactionTypeName { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public long BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;

    public decimal InwardQty { get; set; }
    public decimal OutwardQty { get; set; }
    public decimal SignedQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }

    /// <summary>Running balance per item, ordered as the journal was written.</summary>
    public decimal RunningBalance { get; set; }

    public decimal BatchRunningBalance { get; set; }

    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
}

/// <summary>vw_CustomerOutstanding - derived dues, never a stored column.</summary>
public class CustomerOutstandingView
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Village { get; set; }
    public string? Mobile { get; set; }
    public string CustomerType { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }
    public bool IsActive { get; set; }

    public decimal OpeningBalance { get; set; }
    public long InvoiceCount { get; set; }
    public decimal TotalBilled { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal UnpaidBalance { get; set; }
    public decimal AdjustedReturns { get; set; }
    public decimal OnAccountAdvance { get; set; }
    public decimal OutstandingAmount { get; set; }

    public DateTime? LastInvoiceDate { get; set; }

    /// <summary>Oldest unpaid bill - the number that decides whether to extend more credit.</summary>
    public DateTime? OldestUnpaidDate { get; set; }

    public int? OldestUnpaidAgeDays { get; set; }
}

/// <summary>
/// vw_CustomerLedger - a Tally-style money ledger derived from the permanent
/// vouchers (opening, posted sales, counter + later receipts, adjusted returns).
/// One row per voucher, with a per-customer running balance. Read-only.
/// </summary>
public class CustomerLedgerView
{
    public int CustomerId { get; set; }
    public long Seq { get; set; }
    public DateTime TransactionDate { get; set; }
    public string VoucherType { get; set; } = string.Empty;
    public string? VoucherNumber { get; set; }
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public string? Narration { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
    public int? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
}

/// <summary>
/// vw_SupplierLedger - the supplier mirror of vw_CustomerLedger (opening, posted
/// purchases, counter + later payments, purchase returns). A supplier is a
/// creditor, so the running balance is negative (CR) when we owe. Read-only.
/// </summary>
public class SupplierLedgerView
{
    public int SupplierId { get; set; }
    public long Seq { get; set; }
    public DateTime TransactionDate { get; set; }
    public string VoucherType { get; set; } = string.Empty;
    public string? VoucherNumber { get; set; }
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public string? Narration { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
    public int? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
}

/// <summary>vw_SupplierOutstanding.</summary>
public class SupplierOutstandingView
{
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Phone { get; set; }
    public int PaymentTermDays { get; set; }
    public decimal CreditLimit { get; set; }
    public bool IsActive { get; set; }

    public decimal OpeningBalance { get; set; }
    public long BillCount { get; set; }
    public decimal TotalPurchased { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal UnpaidBalance { get; set; }
    public decimal ReturnValue { get; set; }
    public decimal OnAccountAdvance { get; set; }
    public decimal OutstandingAmount { get; set; }

    public DateTime? LastPurchaseDate { get; set; }
    public DateTime? OldestUnpaidDate { get; set; }
    public DateTime? NextDueDate { get; set; }
}

/// <summary>vw_ItemSubGroupWiseStock - dashboard donut chart.</summary>
public class ItemSubGroupWiseStockView
{
    public int ItemSubGroupId { get; set; }
    public string ItemSubGroupName { get; set; } = string.Empty;
    public int? ParentItemSubGroupId { get; set; }
    public long ItemCount { get; set; }
    public int InStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int LowStockCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal StockValueAtCost { get; set; }
    public decimal StockValueAtMrp { get; set; }
}

/// <summary>vw_DailySalesSummary - feeds the monthly sales graph.</summary>
public class DailySalesSummaryView
{
    public DateTime InvoiceDate { get; set; }
    public long InvoiceCount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalCost { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal AmountReceived { get; set; }
    public decimal CreditGiven { get; set; }
    public int CashInvoiceCount { get; set; }
    public int CreditInvoiceCount { get; set; }
}

/// <summary>vw_DailyPurchaseSummary - feeds the purchase graph.</summary>
public class DailyPurchaseSummaryView
{
    public DateTime PurchaseDate { get; set; }
    public long BillCount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalPurchase { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
}
