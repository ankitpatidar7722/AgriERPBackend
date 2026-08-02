/*==============================================================================
  AgriERP  |  10_Views.sql
  ------------------------------------------------------------------------------
  Reporting views. Each one exists because the alternative was a stored column
  that would eventually disagree with the transactions behind it.

  A NOTE ON DATES
  ---------------
  Audit timestamps (CreatedAt, LoggedAt) are UTC via SYSUTCDATETIME(): they
  record when a machine did something and must survive timezone changes.
  Business dates (InvoiceDate, ExpiryDate) are local calendar dates - a bill
  raised at 11pm IST belongs to that day's sales, not the next day's in UTC.
  These views therefore compare against CAST(GETDATE() AS DATE).
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*==============================================================================
  vw_ItemStock  - current stock per product, rolled up from batches
==============================================================================*/
CREATE OR ALTER VIEW vw_ItemStock
AS
SELECT
    p.ItemId,
    p.ItemCode,
    p.ItemName,
    p.ShortName,
    p.ItemSubGroupId,
    c.ItemSubGroupName,
    p.CompanyId,
    co.CompanyName,
    p.UnitId,
    u.UnitCode,
    p.MinStockLevel,
    p.MaxStockLevel,
    p.ReorderLevel,
    p.PurchaseRate,
    p.SellingRate,
    p.Mrp,
    p.RackNumber,
    p.IsActive,

    ISNULL(s.CurrentStock, 0)   AS CurrentStock,
    ISNULL(s.BatchCount,   0)   AS BatchCount,
    -- Valued at each batch's own landed cost, not the product's current rate.
    -- Those differ whenever prices moved between consignments.
    ISNULL(s.StockValueAtCost, 0) AS StockValueAtCost,
    ISNULL(s.StockValueAtMrp,  0) AS StockValueAtMrp,
    s.NearestExpiryDate,

    CASE
        WHEN ISNULL(s.CurrentStock, 0) <= 0                 THEN N'OutOfStock'
        WHEN ISNULL(s.CurrentStock, 0) <= p.MinStockLevel   THEN N'LowStock'
        WHEN p.MaxStockLevel > 0
             AND ISNULL(s.CurrentStock, 0) >= p.MaxStockLevel THEN N'OverStock'
        ELSE N'Normal'
    END AS StockStatus
FROM ItemMaster AS p
INNER JOIN ItemSubGroupMaster AS c  ON c.ItemSubGroupId = p.ItemSubGroupId
LEFT  JOIN Companies  AS co ON co.CompanyId = p.CompanyId
INNER JOIN Units      AS u  ON u.UnitId     = p.UnitId
OUTER APPLY
(
    SELECT
        SUM(b.CurrentQty)                                       AS CurrentStock,
        COUNT_BIG(*)                                            AS BatchCount,
        CAST(SUM(b.CurrentQty * b.PurchaseRate) AS DECIMAL(18,2)) AS StockValueAtCost,
        CAST(SUM(b.CurrentQty * b.Mrp)          AS DECIMAL(18,2)) AS StockValueAtMrp,
        MIN(CASE WHEN b.CurrentQty > 0 THEN b.ExpiryDate END)   AS NearestExpiryDate
    FROM ItemBatches AS b
    WHERE b.ItemId = p.ItemId
      AND b.CurrentQty <> 0
) AS s
WHERE p.IsDeleted = 0;
GO

/*==============================================================================
  vw_BatchStock  - batch-wise stock with expiry classification
==============================================================================*/
CREATE OR ALTER VIEW vw_BatchStock
AS
SELECT
    b.BatchId,
    b.ItemId,
    p.ItemCode,
    p.ItemName,
    p.ItemSubGroupId,
    c.ItemSubGroupName,
    p.CompanyId,
    co.CompanyName,
    b.BatchNumber,
    b.LocationId,
    l.LocationName,
    b.ManufacturingDate,
    b.ExpiryDate,
    b.PurchaseRate,
    b.SellingRate,
    b.Mrp,
    b.InwardQty,
    b.OutwardQty,
    b.CurrentQty,
    CAST(b.CurrentQty * b.PurchaseRate AS DECIMAL(18,2)) AS StockValueAtCost,
    u.UnitCode,

    DATEDIFF(DAY, CAST(GETDATE() AS DATE), b.ExpiryDate) AS DaysToExpiry,
    CASE
        WHEN b.ExpiryDate IS NULL                                              THEN N'NoExpiry'
        WHEN b.ExpiryDate <  CAST(GETDATE() AS DATE)                           THEN N'Expired'
        WHEN b.ExpiryDate <= DATEADD(DAY, 30,  CAST(GETDATE() AS DATE))        THEN N'Critical'
        WHEN b.ExpiryDate <= DATEADD(DAY, 90,  CAST(GETDATE() AS DATE))        THEN N'Warning'
        ELSE N'Safe'
    END AS ExpiryStatus
FROM ItemBatches AS b
INNER JOIN ItemMaster         AS p  ON p.ItemId  = b.ItemId
INNER JOIN ItemSubGroupMaster       AS c  ON c.ItemSubGroupId = p.ItemSubGroupId
LEFT  JOIN Companies        AS co ON co.CompanyId = p.CompanyId
INNER JOIN Units            AS u  ON u.UnitId     = p.UnitId
INNER JOIN StorageLocations AS l  ON l.LocationId = b.LocationId
WHERE p.IsDeleted = 0;
GO

/*==============================================================================
  vw_StockLedger  - the movement journal with a running balance

  This is the "StockLedger" from your table list. It is a view rather than a
  table because a second physical copy of every movement would be duplicate
  data that has to be kept in step with the journal forever.
==============================================================================*/
CREATE OR ALTER VIEW vw_StockLedger
AS
SELECT
    st.StockTransactionId,
    st.TransactionDate,
    st.TransactionTypeId,
    tt.TypeCode        AS TransactionTypeCode,
    tt.TypeName        AS TransactionTypeName,
    st.ItemId,
    p.ItemCode,
    p.ItemName,
    st.BatchId,
    b.BatchNumber,
    b.ExpiryDate,
    st.LocationId,
    l.LocationName,
    u.UnitCode,

    CASE WHEN st.Direction =  1 THEN st.Quantity ELSE 0 END AS InwardQty,
    CASE WHEN st.Direction = -1 THEN st.Quantity ELSE 0 END AS OutwardQty,
    st.SignedQuantity,
    st.Rate,
    st.Value,

    -- Running balance per product, ordered the way the journal was written.
    -- StockTransactionId is the tie-breaker so two movements stamped with the
    -- same instant still produce a stable, reproducible sequence.
    SUM(st.SignedQuantity) OVER (
        PARTITION BY st.ItemId
        ORDER BY st.TransactionDate, st.StockTransactionId
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningBalance,

    -- Same balance, narrowed to one batch.
    SUM(st.SignedQuantity) OVER (
        PARTITION BY st.BatchId
        ORDER BY st.TransactionDate, st.StockTransactionId
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS BatchRunningBalance,

    st.ReferenceType,
    st.ReferenceId,
    st.ReferenceNumber,
    st.Remarks,
    st.CreatedAt,
    st.CreatedBy
FROM StockTransactions AS st
INNER JOIN TransactionTypes AS tt ON tt.TransactionTypeId = st.TransactionTypeId
INNER JOIN ItemMaster         AS p  ON p.ItemId  = st.ItemId
INNER JOIN ItemBatches   AS b  ON b.BatchId    = st.BatchId
INNER JOIN StorageLocations AS l  ON l.LocationId = st.LocationId
INNER JOIN Units            AS u  ON u.UnitId     = p.UnitId;
GO

/*==============================================================================
  vw_LowStockItems  /  vw_OutOfStockItems
==============================================================================*/
CREATE OR ALTER VIEW vw_LowStockItems
AS
SELECT
    ps.*,
    -- How much to order to reach the maximum level, or the minimum if no
    -- maximum has been configured.
    CASE
        WHEN ps.MaxStockLevel > 0 THEN ps.MaxStockLevel - ps.CurrentStock
        ELSE ps.MinStockLevel - ps.CurrentStock
    END AS SuggestedOrderQty
FROM vw_ItemStock AS ps
WHERE ps.IsActive = 1
  AND ps.CurrentStock > 0
  AND ps.CurrentStock <= ps.MinStockLevel;
GO

CREATE OR ALTER VIEW vw_OutOfStockItems
AS
SELECT ps.*
FROM vw_ItemStock AS ps
WHERE ps.IsActive = 1
  AND ps.CurrentStock <= 0;
GO

/*==============================================================================
  vw_NearExpiryStock  /  vw_ExpiredStock
==============================================================================*/
CREATE OR ALTER VIEW vw_NearExpiryStock
AS
SELECT bs.*
FROM vw_BatchStock AS bs
WHERE bs.CurrentQty > 0
  AND bs.ExpiryStatus IN (N'Critical', N'Warning');
GO

CREATE OR ALTER VIEW vw_ExpiredStock
AS
SELECT bs.*
FROM vw_BatchStock AS bs
WHERE bs.CurrentQty > 0
  AND bs.ExpiryStatus = N'Expired';
GO

/*==============================================================================
  vw_CustomerOutstanding

  Outstanding =  opening balance (signed)
              +  unpaid balance on posted invoices
              -  sales returns not refunded in cash (i.e. adjusted in ledger)
              -  receipts taken on account and not yet allocated to a bill

  Sales.BalanceAmount is authoritative for a single invoice: it already nets
  off both counter collections and later allocated receipts. Adding the full
  value of Payments here would double-count every rupee.
==============================================================================*/
CREATE OR ALTER VIEW vw_CustomerOutstanding
AS
SELECT
    cu.CustomerId,
    cu.CustomerCode,
    cu.CustomerName,
    cu.Village,
    cu.Mobile,
    cu.CustomerType,
    cu.CreditLimit,
    cu.CreditDays,
    cu.IsActive,

    CAST(CASE WHEN cu.OpeningBalanceType = 'DR' THEN cu.OpeningBalance
              ELSE -cu.OpeningBalance END AS DECIMAL(18,2)) AS OpeningBalance,

    ISNULL(InvoiceCount,     0) AS InvoiceCount,
    ISNULL(TotalBilled,      0) AS TotalBilled,
    ISNULL(TotalReceived,    0) AS TotalReceived,
    ISNULL(UnpaidBalance,    0) AS UnpaidBalance,
    ISNULL(ret.AdjustedReturns,  0) AS AdjustedReturns,
    ISNULL(adv.OnAccountAdvance, 0) AS OnAccountAdvance,

    CAST(
        CASE WHEN cu.OpeningBalanceType = 'DR' THEN cu.OpeningBalance ELSE -cu.OpeningBalance END
        + ISNULL(UnpaidBalance,   0)
        - ISNULL(ret.AdjustedReturns, 0)
        - ISNULL(adv.OnAccountAdvance,0)
    AS DECIMAL(18,2)) AS OutstandingAmount,

    LastInvoiceDate,
    -- Oldest unpaid bill: the number that decides whether to extend more credit.
    OldestUnpaidDate,
    DATEDIFF(DAY, OldestUnpaidDate, CAST(GETDATE() AS DATE)) AS OldestUnpaidAgeDays
FROM Customers AS cu
OUTER APPLY
(
    SELECT COUNT_BIG(*)          AS InvoiceCount,
           SUM(s.GrandTotal)     AS TotalBilled,
           SUM(s.ReceivedAmount) AS TotalReceived,
           SUM(s.BalanceAmount)  AS UnpaidBalance,
           MAX(s.InvoiceDate)    AS LastInvoiceDate,
           MIN(CASE WHEN s.BalanceAmount > 0 THEN s.InvoiceDate END) AS OldestUnpaidDate
    FROM Sales AS s
    WHERE s.CustomerId = cu.CustomerId
      AND s.Status = 'Posted'
) AS inv
OUTER APPLY
(
    SELECT SUM(sr.GrandTotal - sr.RefundedAmount) AS AdjustedReturns
    FROM SalesReturns AS sr
    WHERE sr.CustomerId = cu.CustomerId
      AND sr.Status = 'Posted'
) AS ret
OUTER APPLY
(
    SELECT SUM(pm.UnallocatedAmount) AS OnAccountAdvance
    FROM Payments AS pm
    WHERE pm.CustomerId  = cu.CustomerId
      AND pm.PartyType   = 'Customer'
      AND pm.PaymentType = 'Receipt'
      AND pm.Status      = 'Posted'
) AS adv
WHERE cu.IsDeleted = 0;
GO

/*==============================================================================
  vw_SupplierOutstanding  - the mirror image; 'CR' opening means we owe them.
==============================================================================*/
CREATE OR ALTER VIEW vw_SupplierOutstanding
AS
SELECT
    su.SupplierId,
    su.SupplierCode,
    su.SupplierName,
    su.City,
    su.Phone,
    su.PaymentTermDays,
    su.CreditLimit,
    su.IsActive,

    CAST(CASE WHEN su.OpeningBalanceType = 'CR' THEN su.OpeningBalance
              ELSE -su.OpeningBalance END AS DECIMAL(18,2)) AS OpeningBalance,

    ISNULL(pu.BillCount,      0) AS BillCount,
    ISNULL(pu.TotalPurchased, 0) AS TotalPurchased,
    ISNULL(pu.TotalPaid,      0) AS TotalPaid,
    ISNULL(pu.UnpaidBalance,  0) AS UnpaidBalance,
    ISNULL(pr.ReturnValue,    0) AS ReturnValue,
    ISNULL(adv.OnAccountAdvance, 0) AS OnAccountAdvance,

    CAST(
        CASE WHEN su.OpeningBalanceType = 'CR' THEN su.OpeningBalance ELSE -su.OpeningBalance END
        + ISNULL(pu.UnpaidBalance,    0)
        - ISNULL(pr.ReturnValue,      0)
        - ISNULL(adv.OnAccountAdvance,0)
    AS DECIMAL(18,2)) AS OutstandingAmount,

    pu.LastPurchaseDate,
    pu.OldestUnpaidDate,
    pu.NextDueDate
FROM Suppliers AS su
OUTER APPLY
(
    SELECT COUNT_BIG(*)         AS BillCount,
           SUM(p.GrandTotal)    AS TotalPurchased,
           SUM(p.PaidAmount)    AS TotalPaid,
           SUM(p.BalanceAmount) AS UnpaidBalance,
           MAX(p.PurchaseDate)  AS LastPurchaseDate,
           MIN(CASE WHEN p.BalanceAmount > 0 THEN p.PurchaseDate END) AS OldestUnpaidDate,
           MIN(CASE WHEN p.BalanceAmount > 0 THEN p.DueDate END)      AS NextDueDate
    FROM Purchases AS p
    WHERE p.SupplierId = su.SupplierId
      AND p.Status = 'Posted'
) AS pu
OUTER APPLY
(
    SELECT SUM(r.GrandTotal) AS ReturnValue
    FROM PurchaseReturns AS r
    WHERE r.SupplierId = su.SupplierId
      AND r.Status = 'Posted'
) AS pr
OUTER APPLY
(
    SELECT SUM(pm.UnallocatedAmount) AS OnAccountAdvance
    FROM Payments AS pm
    WHERE pm.SupplierId  = su.SupplierId
      AND pm.PartyType   = 'Supplier'
      AND pm.PaymentType = 'Payment'
      AND pm.Status      = 'Posted'
) AS adv
WHERE su.IsDeleted = 0;
GO

/*==============================================================================
  vw_ItemSubGroupWiseStock  /  vw_CompanyWiseStock  - dashboard donut charts
==============================================================================*/
CREATE OR ALTER VIEW vw_ItemSubGroupWiseStock
AS
SELECT
    c.ItemSubGroupId,
    c.ItemSubGroupName,
    c.ParentItemSubGroupId,
    COUNT_BIG(ps.ItemId)                              AS ItemCount,
    SUM(CASE WHEN ps.CurrentStock > 0 THEN 1 ELSE 0 END) AS InStockCount,
    SUM(CASE WHEN ps.CurrentStock <= 0 THEN 1 ELSE 0 END) AS OutOfStockCount,
    SUM(CASE WHEN ps.StockStatus = N'LowStock' THEN 1 ELSE 0 END) AS LowStockCount,
    -- ISNULL is required, not cosmetic: this is a LEFT JOIN, so a category
    -- with no products yields SUM(NULL) = NULL. A stock report must show an
    -- empty category as 0.00, and a NULL here also fails to materialise into
    -- the non-nullable decimals on CategoryWiseStockView.
    CAST(ISNULL(SUM(ps.CurrentStock),     0) AS DECIMAL(18,3)) AS TotalQuantity,
    CAST(ISNULL(SUM(ps.StockValueAtCost), 0) AS DECIMAL(18,2)) AS StockValueAtCost,
    CAST(ISNULL(SUM(ps.StockValueAtMrp),  0) AS DECIMAL(18,2)) AS StockValueAtMrp
FROM ItemSubGroupMaster AS c
LEFT JOIN vw_ItemStock AS ps
       ON ps.ItemSubGroupId = c.ItemSubGroupId AND ps.IsActive = 1
WHERE c.IsDeleted = 0
GROUP BY c.ItemSubGroupId, c.ItemSubGroupName, c.ParentItemSubGroupId;
GO

CREATE OR ALTER VIEW vw_CompanyWiseStock
AS
SELECT
    co.CompanyId,
    co.CompanyName,
    COUNT_BIG(ps.ItemId)                                     AS ItemCount,
    -- Same LEFT JOIN NULL problem as vw_ItemSubGroupWiseStock above.
    CAST(ISNULL(SUM(ps.CurrentStock),     0) AS DECIMAL(18,3))  AS TotalQuantity,
    CAST(ISNULL(SUM(ps.StockValueAtCost), 0) AS DECIMAL(18,2))  AS StockValueAtCost,
    CAST(ISNULL(SUM(ps.StockValueAtMrp),  0) AS DECIMAL(18,2))  AS StockValueAtMrp
FROM Companies AS co
LEFT JOIN vw_ItemStock AS ps
       ON ps.CompanyId = co.CompanyId AND ps.IsActive = 1
WHERE co.IsDeleted = 0
GROUP BY co.CompanyId, co.CompanyName;
GO

/*==============================================================================
  vw_DailySalesSummary  - one row per day, feeds the monthly sales graph
==============================================================================*/
CREATE OR ALTER VIEW vw_DailySalesSummary
AS
SELECT
    s.InvoiceDate,
    COUNT_BIG(*)                                             AS InvoiceCount,
    CAST(SUM(s.TaxableAmount)   AS DECIMAL(18,2))            AS TaxableAmount,
    CAST(SUM(s.CgstAmount + s.SgstAmount + s.IgstAmount + s.CessAmount) AS DECIMAL(18,2)) AS TaxAmount,
    CAST(SUM(s.GrandTotal)      AS DECIMAL(18,2))            AS TotalSales,
    CAST(SUM(s.TotalCostAmount) AS DECIMAL(18,2))            AS TotalCost,
    CAST(SUM(s.GrossProfit)     AS DECIMAL(18,2))            AS GrossProfit,
    CAST(SUM(s.ReceivedAmount)  AS DECIMAL(18,2))            AS AmountReceived,
    CAST(SUM(s.BalanceAmount)   AS DECIMAL(18,2))            AS CreditGiven,
    SUM(CASE WHEN s.PaymentType = 'Cash'   THEN 1 ELSE 0 END) AS CashInvoiceCount,
    SUM(CASE WHEN s.PaymentType = 'Credit' THEN 1 ELSE 0 END) AS CreditInvoiceCount
FROM Sales AS s
WHERE s.Status = 'Posted'
GROUP BY s.InvoiceDate;
GO

/*==============================================================================
  vw_DailyPurchaseSummary  - feeds the purchase graph
==============================================================================*/
CREATE OR ALTER VIEW vw_DailyPurchaseSummary
AS
SELECT
    p.PurchaseDate,
    COUNT_BIG(*)                                  AS BillCount,
    CAST(SUM(p.TaxableAmount) AS DECIMAL(18,2))   AS TaxableAmount,
    CAST(SUM(p.CgstAmount + p.SgstAmount + p.IgstAmount + p.CessAmount) AS DECIMAL(18,2)) AS TaxAmount,
    CAST(SUM(p.GrandTotal)    AS DECIMAL(18,2))   AS TotalPurchase,
    CAST(SUM(p.PaidAmount)    AS DECIMAL(18,2))   AS AmountPaid,
    CAST(SUM(p.BalanceAmount) AS DECIMAL(18,2))   AS AmountDue
FROM Purchases AS p
WHERE p.Status = 'Posted'
GROUP BY p.PurchaseDate;
GO

/*==============================================================================
  vw_GstSalesSummary  - HSN-wise, rate-wise output tax for GSTR-1 / GSTR-3B
==============================================================================*/
CREATE OR ALTER VIEW vw_GstSalesSummary
AS
SELECT
    s.InvoiceDate,
    sd.HsnCode,
    sd.GstPercent,
    s.IsInterState,
    COUNT_BIG(DISTINCT s.SaleId)                    AS InvoiceCount,
    CAST(SUM(sd.TotalQuantity) AS DECIMAL(18,3))    AS TotalQuantity,
    CAST(SUM(sd.TaxableAmount) AS DECIMAL(18,2))    AS TaxableAmount,
    CAST(SUM(sd.CgstAmount)    AS DECIMAL(18,2))    AS CgstAmount,
    CAST(SUM(sd.SgstAmount)    AS DECIMAL(18,2))    AS SgstAmount,
    CAST(SUM(sd.IgstAmount)    AS DECIMAL(18,2))    AS IgstAmount,
    CAST(SUM(sd.CessAmount)    AS DECIMAL(18,2))    AS CessAmount,
    CAST(SUM(sd.LineTotal)     AS DECIMAL(18,2))    AS TotalAmount
FROM Sales AS s
INNER JOIN SalesDetails AS sd ON sd.SaleId = s.SaleId
WHERE s.Status = 'Posted'
GROUP BY s.InvoiceDate, sd.HsnCode, sd.GstPercent, s.IsInterState;
GO

/*==============================================================================
  vw_GstPurchaseSummary  - input tax credit side, for GSTR-2 reconciliation
==============================================================================*/
CREATE OR ALTER VIEW vw_GstPurchaseSummary
AS
SELECT
    p.PurchaseDate,
    pd.HsnCode,
    pd.GstPercent,
    p.IsInterState,
    COUNT_BIG(DISTINCT p.PurchaseId)                AS BillCount,
    CAST(SUM(pd.TotalQuantity) AS DECIMAL(18,3))    AS TotalQuantity,
    CAST(SUM(pd.TaxableAmount) AS DECIMAL(18,2))    AS TaxableAmount,
    CAST(SUM(pd.CgstAmount)    AS DECIMAL(18,2))    AS CgstAmount,
    CAST(SUM(pd.SgstAmount)    AS DECIMAL(18,2))    AS SgstAmount,
    CAST(SUM(pd.IgstAmount)    AS DECIMAL(18,2))    AS IgstAmount,
    CAST(SUM(pd.CessAmount)    AS DECIMAL(18,2))    AS CessAmount,
    CAST(SUM(pd.LineTotal)     AS DECIMAL(18,2))    AS TotalAmount
FROM Purchases AS p
INNER JOIN PurchaseDetails AS pd ON pd.PurchaseId = p.PurchaseId
WHERE p.Status = 'Posted'
GROUP BY p.PurchaseDate, pd.HsnCode, pd.GstPercent, p.IsInterState;
GO

PRINT N'10_Views.sql completed.';
GO
