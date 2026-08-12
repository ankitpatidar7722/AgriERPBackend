/*==============================================================================
  AgriERP  |  32_SupplierLedger.sql
  ------------------------------------------------------------------------------
  A Tally-style supplier money ledger, the mirror of vw_CustomerLedger, DERIVED
  from the permanent vouchers that already exist (Suppliers.OpeningBalance,
  posted Purchases/GRN, posted supplier Payments, posted PurchaseReturns).
  Nothing is stored twice, so it reconciles to vw_SupplierOutstanding BY
  CONSTRUCTION.

  A supplier is a CREDITOR, so the sign is the mirror of a customer:
    Credit increases what WE owe the supplier (Purchase bill, Opening CR).
    Debit  reduces it (payment we made, purchase return, Opening DR advance).
  RunningBalance = signed cumulative (Debit - Credit) per supplier in
  chronological order. A supplier we owe carries a CR balance (negative), so
  the last value per supplier is -(OutstandingAmount) - the same magnitude
  vw_SupplierOutstanding computes.

  Also adds the "Supplier Ledger" entry to the Accounts menu group.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 32_SupplierLedger ---';
GO

CREATE OR ALTER VIEW vw_SupplierLedger
AS
WITH entries AS
(
    /* -------- Opening balance (CR = we owe the supplier, DR = advance) --------
       Ordering per supplier: business date, then the source voucher's CreatedAt
       (SortTime), then a type tiebreaker so a counter payment sits right after
       its own bill. */
    SELECT
        s.SupplierId,
        CAST(ISNULL(s.OpeningBalanceDate, CAST(s.CreatedAt AS DATE)) AS DATE) AS TransactionDate,
        N'Opening Balance'                       AS VoucherType,
        CAST(NULL AS NVARCHAR(30))               AS VoucherNumber,
        N'Opening'                               AS ReferenceType,
        CAST(NULL AS BIGINT)                     AS ReferenceId,
        N'Opening balance'                       AS Narration,
        CAST(CASE WHEN s.OpeningBalanceType = 'DR' THEN s.OpeningBalance ELSE 0 END AS DECIMAL(18,2)) AS Debit,
        CAST(CASE WHEN s.OpeningBalanceType = 'CR' THEN s.OpeningBalance ELSE 0 END AS DECIMAL(18,2)) AS Credit,
        0                                        AS TypeOrder,
        CAST(0 AS BIGINT)                        AS RefKey,
        s.CreatedBy,
        CAST(s.CreatedAt AS DATETIME2(3))        AS SortTime
    FROM Suppliers s
    WHERE s.IsDeleted = 0 AND s.OpeningBalance <> 0

    UNION ALL
    /* ------------- Purchase (GRN) bill: full value CREDIT ------------------ */
    SELECT
        p.SupplierId,
        CAST(p.PurchaseDate AS DATE),
        N'Purchase Bill',
        p.PurchaseNumber,
        N'Purchase',
        CAST(p.PurchaseId AS BIGINT),
        N'Purchase Bill ' + p.PurchaseNumber,
        CAST(0 AS DECIMAL(18,2)),
        CAST(p.GrandTotal AS DECIMAL(18,2)),
        1,
        CAST(p.PurchaseId AS BIGINT),
        p.CreatedBy,
        CAST(p.CreatedAt AS DATETIME2(3))
    FROM Purchases p
    WHERE p.Status = 'Posted'

    UNION ALL
    /* --- Counter payment made AT the GRN (paid minus later receipts allocated to it): DEBIT --- */
    SELECT
        p.SupplierId,
        CAST(p.PurchaseDate AS DATE),
        N'Payment',
        p.PurchaseNumber,
        N'PurchasePayment',
        CAST(p.PurchaseId AS BIGINT),
        N'Paid against ' + p.PurchaseNumber,
        CAST(p.PaidAmount - ISNULL(a.Allocated, 0) AS DECIMAL(18,2)),
        CAST(0 AS DECIMAL(18,2)),
        2,
        CAST(p.PurchaseId AS BIGINT),
        p.CreatedBy,
        CAST(p.CreatedAt AS DATETIME2(3))
    FROM Purchases p
    OUTER APPLY (
        SELECT SUM(pa.AllocatedAmount) AS Allocated
        FROM PaymentAllocations pa
        WHERE pa.ReferenceType = N'Purchase' AND pa.ReferenceId = p.PurchaseId
    ) a
    WHERE p.Status = 'Posted'
      AND (p.PaidAmount - ISNULL(a.Allocated, 0)) > 0

    UNION ALL
    /* -------------- Payment made to supplier: full value DEBIT ------------- */
    SELECT
        pm.SupplierId,
        CAST(pm.PaymentDate AS DATE),
        N'Payment Made',
        pm.VoucherNumber,
        N'Payment',
        CAST(pm.PaymentId AS BIGINT),
        N'Payment ' + pm.VoucherNumber,
        CAST(pm.Amount AS DECIMAL(18,2)),
        CAST(0 AS DECIMAL(18,2)),
        3,
        CAST(pm.PaymentId AS BIGINT),
        pm.CreatedBy,
        CAST(pm.CreatedAt AS DATETIME2(3))
    FROM Payments pm
    WHERE pm.PartyType = N'Supplier' AND pm.PaymentType = N'Payment'
      AND pm.Status = N'Posted' AND pm.SupplierId IS NOT NULL

    UNION ALL
    /* ---------------- Purchase return: full value DEBIT -------------------- */
    SELECT
        pr.SupplierId,
        CAST(pr.ReturnDate AS DATE),
        N'Purchase Return',
        pr.ReturnNumber,
        N'PurchaseReturn',
        CAST(pr.PurchaseReturnId AS BIGINT),
        N'Purchase Return ' + pr.ReturnNumber,
        CAST(pr.GrandTotal AS DECIMAL(18,2)),
        CAST(0 AS DECIMAL(18,2)),
        4,
        CAST(pr.PurchaseReturnId AS BIGINT),
        pr.CreatedBy,
        CAST(pr.CreatedAt AS DATETIME2(3))
    FROM PurchaseReturns pr
    WHERE pr.Status = 'Posted'
),
ranked AS
(
    SELECT
        e.*,
        SUM(e.Debit - e.Credit) OVER (
            PARTITION BY e.SupplierId
            ORDER BY e.TransactionDate, e.SortTime, e.TypeOrder, e.RefKey
            ROWS UNBOUNDED PRECEDING
        ) AS RunningBalance,
        ROW_NUMBER() OVER (
            PARTITION BY e.SupplierId
            ORDER BY e.TransactionDate, e.SortTime, e.TypeOrder, e.RefKey
        ) AS Seq
    FROM entries e
)
SELECT
    r.SupplierId,
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
/* Menu: add "Supplier Ledger" to the existing Accounts group                 */
/*----------------------------------------------------------------------------*/
DECLARE @acctHead INT = 5;   -- the Accounts group added in 26_CustomerLedger.

IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/accounts/supplier-ledger')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/accounts/supplier-ledger', N'Supplier Ledger', N'Accounts', N'Accounts',
         @acctHead, 4, @acctHead, N'BookOpen', 0, SYSUTCDATETIME());
GO

/*==============================================================================
  VERIFY  (view compiles + reconciles to vw_SupplierOutstanding)
  Ledger RunningBalance is signed (Debit-Credit), i.e. -(OutstandingAmount),
  so we check ABS(LedgerBal + OutstandingAmount).
==============================================================================*/
IF OBJECT_ID(N'vw_SupplierLedger', N'V') IS NULL
BEGIN
    PRINT N'RESULT: 32_SupplierLedger FAILED - view not created.';
END
ELSE
BEGIN
    DECLARE @mismatch INT = (
        SELECT COUNT(*)
        FROM (
            SELECT l.SupplierId, SUM(l.Debit - l.Credit) AS LedgerBal
            FROM vw_SupplierLedger l GROUP BY l.SupplierId
        ) L
        FULL JOIN vw_SupplierOutstanding o ON o.SupplierId = L.SupplierId
        WHERE ABS(ISNULL(L.LedgerBal, 0) + ISNULL(o.OutstandingAmount, 0)) > 0.01
    );
    IF @mismatch = 0
        PRINT N'RESULT: 32_SupplierLedger completed - ledger view reconciles to vw_SupplierOutstanding, Supplier Ledger menu added.';
    ELSE
        PRINT N'RESULT: 32_SupplierLedger WARNING - ' + CAST(@mismatch AS NVARCHAR(10)) + N' supplier(s) differ from vw_SupplierOutstanding.';
END
GO
