using AgriERP.Domain.Common;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.Finance;

/// <summary>Maps to PaymentModes.</summary>
public class PaymentMode
{
    public int PaymentModeId { get; set; }
    public string ModeCode { get; set; } = string.Empty;    // CASH, UPI, CHEQUE
    public string ModeName { get; set; } = string.Empty;

    /// <summary>Cheques and NEFT need a reference number; cash does not.</summary>
    public bool RequiresReference { get; set; }

    public bool IsBankMode { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Maps to Payments - one table for both directions.
///
/// Money received from a farmer and money paid to a distributor are the same
/// event with opposite signs, and both need allocation, on-account balances,
/// cheque tracking and cancellation. Two near-identical tables would mean
/// writing the ageing logic twice and fixing every bug twice.
///
/// All four PartyType/PaymentType combinations are legitimate: a supplier
/// refunding a rejected consignment is Supplier + Receipt.
/// </summary>
public class Payment : DocumentEntity
{
    public long PaymentId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }

    public PartyType PartyType { get; set; }
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }

    /// <summary>Receipt is money in, Payment is money out.</summary>
    public PaymentDirection PaymentType { get; set; }

    public int PaymentModeId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Maintained as allocations are added.</summary>
    public decimal AllocatedAmount { get; set; }

    /// <summary>
    /// PERSISTED computed column: Amount - AllocatedAmount. A genuine
    /// on-account advance, not silently spread across bills.
    /// </summary>
    public decimal UnallocatedAmount { get; private set; }

    public string? ReferenceNumber { get; set; }
    public string? BankName { get; set; }
    public DateTime? ChequeDate { get; set; }

    /// <summary>A bounced cheque must reopen the bill it was applied to.</summary>
    public ChequeClearanceStatus ClearanceStatus { get; set; } = ChequeClearanceStatus.Cleared;

    public DateTime? ClearedDate { get; set; }
    public string? Remarks { get; set; }
    public PaymentRecordStatus Status { get; set; } = PaymentRecordStatus.Posted;
    public DateTime? CancelledAt { get; set; }
    public int? CancelledBy { get; set; }
    public string? CancelReason { get; set; }

    public Customer? Customer { get; set; }
    public Supplier? Supplier { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}

/// <summary>
/// Maps to PaymentAllocations.
///
/// A farmer hands over 5,000 against three old bills. This records how that
/// 5,000 was split, which is what makes bill-wise outstanding and ageing
/// possible at all.
/// </summary>
public class PaymentAllocation
{
    public long PaymentAllocationId { get; set; }
    public long PaymentId { get; set; }

    /// <summary>Polymorphic, like the stock journal: invoices and notes alike.</summary>
    public AllocationReferenceType ReferenceType { get; set; }

    public long ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal AllocatedAmount { get; set; }
    public DateTime AllocatedAt { get; set; }
    public int? AllocatedBy { get; set; }

    public Payment? Payment { get; set; }
}

/// <summary>Maps to ExpenseCategories.</summary>
public class ExpenseCategory : MasterEntity
{
    public int ExpenseCategoryId { get; set; }
    // ExpenseCategories has nothing to do with item classification - these
    // are expense heads like Rent and Freight, and their columns keep the
    // "Category" name they have in the database.
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}

/// <summary>Maps to Expenses - shop running costs, for the profit report.</summary>
public class Expense : DocumentEntity
{
    public long ExpenseId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
    public int ExpenseCategoryId { get; set; }
    public int PaymentModeId { get; set; }
    public string? PaidTo { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Input credit on a GST expense bill; zero for wages and tea.</summary>
    public decimal GstAmount { get; set; }

    /// <summary>PERSISTED computed column: Amount + GstAmount.</summary>
    public decimal TotalAmount { get; private set; }

    public string? ReferenceNumber { get; set; }
    public string? BillNumber { get; set; }
    public string? AttachmentPath { get; set; }
    public string? Description { get; set; }
    public PaymentRecordStatus Status { get; set; } = PaymentRecordStatus.Posted;

    public ExpenseCategory? ExpenseCategory { get; set; }
    public PaymentMode? PaymentMode { get; set; }
}
