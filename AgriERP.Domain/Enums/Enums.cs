namespace AgriERP.Domain.Enums;

/*
 * These are stored in SQL Server as NVARCHAR, not as integers, and mapped with
 * HasConversion<string>(). Two reasons:
 *
 *   1. The database stays readable. "SELECT * FROM Sales WHERE Status =
 *      'Posted'" works from SSMS without a lookup table in your head, which
 *      matters when a shop owner's accountant queries the data directly.
 *   2. The CHECK constraints in the SQL scripts already spell out the legal
 *      values. Storing integers would put the enum's meaning in C# only, and
 *      the database would no longer be able to defend itself.
 *
 * Renaming a member is therefore a breaking change: update the matching CHECK
 * constraint in database/scripts/ in the same commit.
 */

/// <summary>Draft touches nothing. Posted writes stock and ledgers. Cancelled reverses them.</summary>
public enum DocumentStatus
{
    Draft,
    Posted,
    Cancelled
}

/// <summary>Which price list applies. Independent of how the bill was settled.</summary>
public enum SaleType
{
    Retail,
    Wholesale,
    Dealer
}

/// <summary>How a bill was settled. Credit requires a named customer.</summary>
public enum SalePaymentType
{
    Cash,
    Credit
}

/// <summary>Derived from amounts by a computed column; never assigned in code.</summary>
public enum PaymentSettlementStatus
{
    Unpaid,
    Partial,
    Paid
}

public enum CustomerType
{
    Retail,
    Wholesale,
    Dealer
}

/// <summary>DR = the party owes us. CR = we owe the party.</summary>
public enum BalanceType
{
    DR,
    CR
}

public enum PartyType
{
    Customer,
    Supplier
}

/// <summary>Receipt is money in, Payment is money out. All four party/direction pairs are legal.</summary>
public enum PaymentDirection
{
    Receipt,
    Payment
}

public enum ChequeClearanceStatus
{
    Pending,
    Cleared,
    Bounced
}

public enum PaymentRecordStatus
{
    Posted,
    Cancelled
}

/// <summary>What a payment or credit note was applied against.</summary>
public enum AllocationReferenceType
{
    Sale,
    Purchase,
    SalesReturn,
    PurchaseReturn
}

public enum PurchaseOrderStatus
{
    Draft,
    Open,
    Partial,
    Received,
    Cancelled
}

/// <summary>Open -> Partial -> Converted as purchase orders consume the requisition.</summary>
public enum PurchaseRequisitionStatus
{
    Draft,
    Open,
    Partial,
    Converted,
    Cancelled
}

public enum StockAdjustmentType
{
    Physical,
    Damage,
    Expiry,
    Opening,
    Other
}

public enum StorageLocationType
{
    Warehouse,
    Godown,
    Rack,
    Shelf,
    Counter
}

/// <summary>Cash back over the counter, or knocked off the customer's ledger.</summary>
public enum SalesReturnRefundMode
{
    Cash,
    Adjust,
    Bank,
    Replacement
}

public enum PriceChangeSource
{
    Manual,
    Purchase,
    Import
}

public enum AuditAction
{
    Insert,
    Update,
    Delete
}

public enum SettingDataType
{
    String,
    Int,
    Decimal,
    Bool,
    Date,
    Json
}

/// <summary>
/// Mirrors TransactionTypes. The integer values are the primary keys
/// seeded by 05_Inventory.sql and are referenced by stored procedures, so they
/// are fixed - never renumber them.
/// </summary>
public enum StockTransactionTypeId : byte
{
    OpeningStock      = 1,
    PurchaseIn        = 2,
    PurchaseReturnOut = 3,
    SalesOut          = 4,
    SalesReturnIn     = 5,
    AdjustmentIn      = 6,
    AdjustmentOut     = 7,
    TransferOut       = 8,
    TransferIn        = 9,
    ExpiryWriteOff    = 10,
    DamageWriteOff    = 11
}

/// <summary>Which document a stock journal row came from.</summary>
public static class StockReferenceType
{
    public const string Purchase        = "Purchase";
    public const string PurchaseReturn  = "PurchaseReturn";
    public const string Sale            = "Sale";
    public const string SalesReturn     = "SalesReturn";
    public const string Adjustment      = "Adjustment";
    public const string Transfer        = "Transfer";
}

/// <summary>
/// Document types recognised by usp_GetNextDocumentNumber. Must match the
/// DocumentType values seeded into NumberSeries.
/// </summary>
public static class DocumentType
{
    public const string Sale             = "Sale";
    public const string SalesReturn      = "SalesReturn";
    public const string Purchase         = "Purchase";
    public const string PurchaseReturn   = "PurchaseReturn";
    public const string PurchaseRequisition = "PurchaseRequisition";
    public const string PurchaseOrder    = "PurchaseOrder";
    public const string StockAdjustment  = "StockAdjustment";
    public const string StockTransfer    = "StockTransfer";
    public const string Receipt          = "Receipt";
    public const string Payment          = "Payment";
    public const string Expense          = "Expense";
    public const string Item          = "Item";
    public const string Customer         = "Customer";
    public const string Supplier         = "Supplier";
    public const string Warehouse        = "Warehouse";
}
