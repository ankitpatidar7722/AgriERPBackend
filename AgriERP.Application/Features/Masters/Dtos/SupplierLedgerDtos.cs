namespace AgriERP.Application.Features.Masters.Dtos;

/// <summary>One line of the supplier's money ledger (a voucher, not a stored row).</summary>
public class SupplierLedgerRowDto
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

/// <summary>
/// A supplier's ledger for a period: opening, the rows, and the closing balance.
/// A supplier we owe carries a CR balance (negative closing); its magnitude is
/// the payable that vw_SupplierOutstanding reports.
/// </summary>
public class SupplierLedgerDto
{
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    /// <summary>Running balance carried into the window (0 when no from-date is given).</summary>
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    /// <summary>Balance at the end of the window; with no filter its magnitude IS the current payable.</summary>
    public decimal ClosingBalance { get; set; }
    public List<SupplierLedgerRowDto> Rows { get; set; } = new();
}
