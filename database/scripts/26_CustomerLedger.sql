/*==============================================================================
  AgriERP  |  26_CustomerLedger.sql
  ------------------------------------------------------------------------------
  A Tally-style customer money ledger, DERIVED from the permanent vouchers that
  already exist (Customers.OpeningBalance, posted Sales, posted customer
  Receipts, posted SalesReturns). Nothing is stored twice, so it can never
  double-count and it reconciles to vw_CustomerOutstanding BY CONSTRUCTION.

  Debit  increases what the customer owes (Sales invoice, Opening DR).
  Credit reduces it (payment received, counter collection, adjusted return).
  RunningBalance = signed cumulative (Debit - Credit) per customer in
  chronological order; the last value per customer IS the outstanding.

  Reconciliation (proven): closing balance
    = signedOpening + SUM(GrandTotal) - SUM(counter receipts) - SUM(all receipts) - adjustedReturns
    = exactly what vw_CustomerOutstanding computes.

  Also adds the "Accounts" menu group (Customer Payment + Customer Ledger).
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 26_CustomerLedger ---';
GO

CREATE OR ALTER VIEW vw_CustomerLedger
AS
WITH entries AS
(
    /* -------- Opening balance (DR = owes us, CR = advance with us) --------
       Ordering per customer is by business date, then actual entry time
       (SortTime = the source voucher's CreatedAt), then a type tiebreaker so a
       counter receipt sits immediately after its own invoice. */
    SELECT
        c.CustomerId,
        CAST(ISNULL(c.OpeningBalanceDate, CAST(c.CreatedAt AS DATE)) AS DATE) AS TransactionDate,
        N'Opening Balance'                       AS VoucherType,
        CAST(NULL AS NVARCHAR(30))               AS VoucherNumber,
        N'Opening'                               AS ReferenceType,
        CAST(NULL AS BIGINT)                     AS ReferenceId,
        N'Opening balance'                       AS Narration,
        CAST(CASE WHEN c.OpeningBalanceType = 'DR' THEN c.OpeningBalance ELSE 0 END AS DECIMAL(18,2)) AS Debit,
        CAST(CASE WHEN c.OpeningBalanceType = 'CR' THEN c.OpeningBalance ELSE 0 END AS DECIMAL(18,2)) AS Credit,
        0                                        AS TypeOrder,
        CAST(0 AS BIGINT)                        AS RefKey,
        c.CreatedBy,
        CAST(c.CreatedAt AS DATETIME2(3))        AS SortTime
    FROM Customers c
    WHERE c.IsDeleted = 0 AND c.OpeningBalance <> 0

    UNION ALL
    /* ------------------- Sales invoice: full value DEBIT ------------------ */
    SELECT
        s.CustomerId,
        CAST(s.InvoiceDate AS DATE),
        N'Sales Invoice',
        s.InvoiceNumber,
        N'Sale',
        CAST(s.SaleId AS BIGINT),
        N'Sales Invoice ' + s.InvoiceNumber,
        CAST(s.GrandTotal AS DECIMAL(18,2)),
        CAST(0 AS DECIMAL(18,2)),
        1,
        CAST(s.SaleId AS BIGINT),
        s.CreatedBy,
        CAST(s.CreatedAt AS DATETIME2(3))
    FROM Sales s
    WHERE s.Status = 'Posted' AND s.CustomerId IS NOT NULL

    UNION ALL
    /* --- Counter collection taken AT the sale (received minus later receipts allocated to it) --- */
    SELECT
        s.CustomerId,
        CAST(s.InvoiceDate AS DATE),
        N'Receipt',
        s.InvoiceNumber,
        N'SaleReceipt',
        CAST(s.SaleId AS BIGINT),
        N'Received against ' + s.InvoiceNumber,
        CAST(0 AS DECIMAL(18,2)),
        CAST(s.ReceivedAmount - ISNULL(a.Allocated, 0) AS DECIMAL(18,2)),
        2,
        CAST(s.SaleId AS BIGINT),
        s.CreatedBy,
        CAST(s.CreatedAt AS DATETIME2(3))
    FROM Sales s
    OUTER APPLY (
        SELECT SUM(pa.AllocatedAmount) AS Allocated
        FROM PaymentAllocations pa
        WHERE pa.ReferenceType = N'Sale' AND pa.ReferenceId = s.SaleId
    ) a
    WHERE s.Status = 'Posted' AND s.CustomerId IS NOT NULL
      AND (s.ReceivedAmount - ISNULL(a.Allocated, 0)) > 0

    UNION ALL
    /* ------------------- Payment receipt: full value CREDIT --------------- */
    SELECT
        p.CustomerId,
        CAST(p.PaymentDate AS DATE),
        N'Payment Receipt',
        p.VoucherNumber,
        N'Payment',
        CAST(p.PaymentId AS BIGINT),
        N'Payment Receipt ' + p.VoucherNumber,
        CAST(0 AS DECIMAL(18,2)),
        CAST(p.Amount AS DECIMAL(18,2)),
        3,
        CAST(p.PaymentId AS BIGINT),
        p.CreatedBy,
        CAST(p.CreatedAt AS DATETIME2(3))
    FROM Payments p
    WHERE p.PartyType = N'Customer' AND p.PaymentType = N'Receipt'
      AND p.Status = N'Posted' AND p.CustomerId IS NOT NULL

    UNION ALL
    /* ----------- Sales return: the credit-adjusted portion, CREDIT -------- */
    SELECT
        sr.CustomerId,
        CAST(sr.ReturnDate AS DATE),
        N'Sales Return',
        sr.ReturnNumber,
        N'SalesReturn',
        CAST(sr.SalesReturnId AS BIGINT),
        N'Sales Return ' + sr.ReturnNumber,
        CAST(0 AS DECIMAL(18,2)),
        CAST(sr.GrandTotal - sr.RefundedAmount AS DECIMAL(18,2)),
        4,
        CAST(sr.SalesReturnId AS BIGINT),
        sr.CreatedBy,
        CAST(sr.CreatedAt AS DATETIME2(3))
    FROM SalesReturns sr
    WHERE sr.Status = 'Posted' AND sr.CustomerId IS NOT NULL
      AND (sr.GrandTotal - sr.RefundedAmount) > 0
),
ranked AS
(
    SELECT
        e.*,
        SUM(e.Debit - e.Credit) OVER (
            PARTITION BY e.CustomerId
            ORDER BY e.TransactionDate, e.SortTime, e.TypeOrder, e.RefKey
            ROWS UNBOUNDED PRECEDING
        ) AS RunningBalance,
        ROW_NUMBER() OVER (
            PARTITION BY e.CustomerId
            ORDER BY e.TransactionDate, e.SortTime, e.TypeOrder, e.RefKey
        ) AS Seq
    FROM entries e
)
SELECT
    r.CustomerId,
    r.Seq,
    r.TransactionDate,
    r.VoucherType,
    r.VoucherNumber,
    r.ReferenceType,
    r.ReferenceId,
    r.Narration,
    r.Debit,
    r.Credit,
    r.RunningBalance,
    r.CreatedBy,
    u.FullName AS CreatedByName
FROM ranked r
LEFT JOIN Users u ON u.UserId = r.CreatedBy;
GO

/*----------------------------------------------------------------------------*/
/* Menu: new "Accounts" group with Customer Payment + Customer Ledger         */
/*----------------------------------------------------------------------------*/
DECLARE @acctHead INT = 5;   -- after Masters (4); a new bottom group, no reorder of existing.

IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/accounts/customer-payment')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/accounts/customer-payment', N'Customer Payment', N'Accounts', N'Accounts',
         @acctHead, 1, @acctHead, N'Wallet', 0, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/accounts/customer-ledger')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/accounts/customer-ledger', N'Customer Ledger', N'Accounts', N'Accounts',
         @acctHead, 2, @acctHead, N'BookOpen', 0, SYSUTCDATETIME());
GO

/*==============================================================================
  VERIFY  (view compiles + reconciles to vw_CustomerOutstanding)
==============================================================================*/
IF OBJECT_ID(N'vw_CustomerLedger', N'V') IS NULL
BEGIN
    PRINT N'RESULT: 26_CustomerLedger FAILED - view not created.';
END
ELSE
BEGIN
    DECLARE @mismatch INT = (
        SELECT COUNT(*)
        FROM (
            SELECT l.CustomerId, SUM(l.Debit - l.Credit) AS LedgerBal
            FROM vw_CustomerLedger l GROUP BY l.CustomerId
        ) L
        FULL JOIN vw_CustomerOutstanding o ON o.CustomerId = L.CustomerId
        WHERE ABS(ISNULL(L.LedgerBal, 0) - ISNULL(o.OutstandingAmount, 0)) > 0.01
    );
    IF @mismatch = 0
        PRINT N'RESULT: 26_CustomerLedger completed - ledger view reconciles to vw_CustomerOutstanding, Accounts menu added.';
    ELSE
        PRINT N'RESULT: 26_CustomerLedger WARNING - ' + CAST(@mismatch AS NVARCHAR(10)) + N' customer(s) differ from vw_CustomerOutstanding.';
END
GO
