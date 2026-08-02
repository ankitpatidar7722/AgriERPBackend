namespace AgriERP.Application.Features.Masters.Dtos;

/// <summary>One line of the customer's money ledger (a voucher, not a stored row).</summary>
public class CustomerLedgerRowDto
{
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
    public string? CreatedByName { get; set; }
}

/// <summary>A customer's ledger for a period: opening, the rows, and the closing balance (= outstanding).</summary>
public class CustomerLedgerDto
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    /// <summary>Running balance carried into the window (0 when no from-date is given).</summary>
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    /// <summary>Balance at the end of the window; with no filter this IS the current outstanding.</summary>
    public decimal ClosingBalance { get; set; }
    public List<CustomerLedgerRowDto> Rows { get; set; } = new();
}

/// <summary>The customer profile header: identity + money summary, all derived from the ledger.</summary>
public class CustomerProfileDto
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public string? Village { get; set; }
    public string CustomerType { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }

    /// <summary>Signed opening (DR positive, CR negative).</summary>
    public decimal OpeningBalance { get; set; }
    /// <summary>Current dues, derived from the ledger (SUM debit − SUM credit).</summary>
    public decimal Outstanding { get; set; }
    public decimal TotalPurchases { get; set; }
    public decimal TotalPayments { get; set; }
    public DateTime? LastPurchaseDate { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool OverCreditLimit { get; set; }
}

/// <summary>Receivables headline for the dashboard: total dues, today's collection, overdue/near-due.</summary>
public class ReceivablesSummaryDto
{
    public decimal TotalOutstanding { get; set; }
    public int OverdueCustomerCount { get; set; }
    public decimal OverdueAmount { get; set; }
    public decimal TodayCollection { get; set; }
    /// <summary>Invoices falling due within the next 7 days.</summary>
    public int NearDueCount { get; set; }
}
