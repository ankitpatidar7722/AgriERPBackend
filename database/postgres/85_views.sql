/*==============================================================================
  AgriERP  |  85_views.sql   (PostgreSQL)
  ------------------------------------------------------------------------------
  Read-model views. PostgreSQL parallel of database/scripts/10_Views.sql
  (+ 26/32/33/34, final state). Created after all tables (80_foreign_keys).

  T-SQL -> PG notes: OUTER APPLY -> LEFT JOIN LATERAL ... ON true;
  ISNULL -> COALESCE; GETDATE()::date -> CURRENT_DATE; DATEDIFF(DAY,a,b) -> (b - a);
  DATEADD(DAY,n,d) -> d + n; string '+' -> ||; N'x' -> 'x'; bit 0/1 -> false/true.
  Base views (vw_ItemStock, vw_BatchStock) come first - others build on them.
==============================================================================*/

/*-------------------------------- vw_ItemStock -----------------------------*/
CREATE VIEW "vw_ItemStock" AS
SELECT
    p."ItemId", p."ItemCode", p."ItemName", p."ShortName",
    p."ItemSubGroupId", c."ItemSubGroupName",
    p."CompanyId", co."CompanyName",
    p."UnitId", u."UnitCode",
    p."MinStockLevel", p."MaxStockLevel", p."ReorderLevel",
    p."PurchaseRate", p."SellingRate", p."Mrp", p."RackNumber", p."IsActive",
    COALESCE(s."CurrentStock", 0)     AS "CurrentStock",
    COALESCE(s."BatchCount", 0)       AS "BatchCount",
    COALESCE(s."StockValueAtCost", 0) AS "StockValueAtCost",
    COALESCE(s."StockValueAtMrp", 0)  AS "StockValueAtMrp",
    s."NearestExpiryDate",
    CASE
        WHEN COALESCE(s."CurrentStock", 0) <= 0                 THEN 'OutOfStock'
        WHEN COALESCE(s."CurrentStock", 0) <= p."MinStockLevel" THEN 'LowStock'
        WHEN p."MaxStockLevel" > 0 AND COALESCE(s."CurrentStock", 0) >= p."MaxStockLevel" THEN 'OverStock'
        ELSE 'Normal'
    END AS "StockStatus"
FROM "ItemMaster" AS p
INNER JOIN "ItemSubGroupMaster" AS c ON c."ItemSubGroupId" = p."ItemSubGroupId"
LEFT  JOIN "Companies"          AS co ON co."CompanyId" = p."CompanyId"
INNER JOIN "Units"              AS u ON u."UnitId" = p."UnitId"
LEFT JOIN LATERAL (
    SELECT
        SUM(b."CurrentQty")                                          AS "CurrentStock",
        count(*)                                                     AS "BatchCount",
        CAST(SUM(b."CurrentQty" * b."PurchaseRate") AS numeric(18,2)) AS "StockValueAtCost",
        CAST(SUM(b."CurrentQty" * b."Mrp")          AS numeric(18,2)) AS "StockValueAtMrp",
        MIN(CASE WHEN b."CurrentQty" > 0 THEN b."ExpiryDate" END)     AS "NearestExpiryDate"
    FROM "ItemBatches" AS b
    WHERE b."ItemId" = p."ItemId" AND b."CurrentQty" <> 0
) AS s ON true
WHERE p."IsDeleted" = false;

/*-------------------------------- vw_BatchStock ----------------------------*/
CREATE VIEW "vw_BatchStock" AS
SELECT
    b."BatchId", b."ItemId", p."ItemCode", p."ItemName",
    p."ItemSubGroupId", c."ItemSubGroupName",
    p."CompanyId", co."CompanyName",
    b."BatchNumber", b."LocationId", l."LocationName",
    b."ManufacturingDate", b."ExpiryDate",
    b."PurchaseRate", b."SellingRate", b."Mrp",
    b."InwardQty", b."OutwardQty", b."CurrentQty",
    CAST(b."CurrentQty" * b."PurchaseRate" AS numeric(18,2)) AS "StockValueAtCost",
    u."UnitCode",
    (b."ExpiryDate" - CURRENT_DATE) AS "DaysToExpiry",
    CASE
        WHEN b."ExpiryDate" IS NULL              THEN 'NoExpiry'
        WHEN b."ExpiryDate" <  CURRENT_DATE      THEN 'Expired'
        WHEN b."ExpiryDate" <= CURRENT_DATE + 30 THEN 'Critical'
        WHEN b."ExpiryDate" <= CURRENT_DATE + 90 THEN 'Warning'
        ELSE 'Safe'
    END AS "ExpiryStatus"
FROM "ItemBatches" AS b
INNER JOIN "ItemMaster"         AS p  ON p."ItemId" = b."ItemId"
INNER JOIN "ItemSubGroupMaster" AS c  ON c."ItemSubGroupId" = p."ItemSubGroupId"
LEFT  JOIN "Companies"          AS co ON co."CompanyId" = p."CompanyId"
INNER JOIN "Units"              AS u  ON u."UnitId" = p."UnitId"
INNER JOIN "StorageLocations"   AS l  ON l."LocationId" = b."LocationId"
WHERE p."IsDeleted" = false;

/*-------------------------------- vw_StockLedger ---------------------------*/
CREATE VIEW "vw_StockLedger" AS
SELECT
    st."StockTransactionId", st."TransactionDate", st."TransactionTypeId",
    tt."TypeCode" AS "TransactionTypeCode", tt."TypeName" AS "TransactionTypeName",
    st."ItemId", p."ItemCode", p."ItemName",
    st."BatchId", b."BatchNumber", b."ExpiryDate",
    st."LocationId", l."LocationName", u."UnitCode",
    CASE WHEN st."Direction" =  1 THEN st."Quantity" ELSE 0 END AS "InwardQty",
    CASE WHEN st."Direction" = -1 THEN st."Quantity" ELSE 0 END AS "OutwardQty",
    st."SignedQuantity", st."Rate", st."Value",
    SUM(st."SignedQuantity") OVER (
        PARTITION BY st."ItemId"
        ORDER BY st."TransactionDate", st."StockTransactionId"
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS "RunningBalance",
    SUM(st."SignedQuantity") OVER (
        PARTITION BY st."BatchId"
        ORDER BY st."TransactionDate", st."StockTransactionId"
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS "BatchRunningBalance",
    st."ReferenceType", st."ReferenceId", st."ReferenceNumber",
    st."Remarks", st."CreatedAt", st."CreatedBy"
FROM "StockTransactions" AS st
INNER JOIN "TransactionTypes" AS tt ON tt."TransactionTypeId" = st."TransactionTypeId"
INNER JOIN "ItemMaster"       AS p  ON p."ItemId" = st."ItemId"
INNER JOIN "ItemBatches"      AS b  ON b."BatchId" = st."BatchId"
INNER JOIN "StorageLocations" AS l  ON l."LocationId" = st."LocationId"
INNER JOIN "Units"            AS u  ON u."UnitId" = p."UnitId";

/*-------------------------------- vw_CustomerLedger ------------------------*/
CREATE VIEW "vw_CustomerLedger" AS
WITH entries AS (
    SELECT
        c."CustomerId",
        CAST(COALESCE(c."OpeningBalanceDate", CAST(c."CreatedAt" AS date)) AS date) AS "TransactionDate",
        'Opening Balance'                   AS "VoucherType",
        CAST(NULL AS varchar(30))           AS "VoucherNumber",
        'Opening'                           AS "ReferenceType",
        CAST(NULL AS bigint)                AS "ReferenceId",
        'Opening balance'                   AS "Narration",
        CAST(CASE WHEN c."OpeningBalanceType" = 'DR' THEN c."OpeningBalance" ELSE 0 END AS numeric(18,2)) AS "Debit",
        CAST(CASE WHEN c."OpeningBalanceType" = 'CR' THEN c."OpeningBalance" ELSE 0 END AS numeric(18,2)) AS "Credit",
        0                                   AS "TypeOrder",
        CAST(0 AS bigint)                   AS "RefKey",
        c."CreatedBy",
        CAST(c."CreatedAt" AS timestamp(3)) AS "SortTime"
    FROM "Customers" c
    WHERE c."IsDeleted" = false AND c."OpeningBalance" <> 0
    UNION ALL
    SELECT s."CustomerId", CAST(s."InvoiceDate" AS date), 'Sales Invoice', s."InvoiceNumber",
        'Sale', CAST(s."SaleId" AS bigint), 'Sales Invoice ' || s."InvoiceNumber",
        CAST(s."GrandTotal" AS numeric(18,2)), CAST(0 AS numeric(18,2)), 1,
        CAST(s."SaleId" AS bigint), s."CreatedBy", CAST(s."CreatedAt" AS timestamp(3))
    FROM "Sales" s
    WHERE s."Status" = 'Posted' AND s."CustomerId" IS NOT NULL
    UNION ALL
    SELECT s."CustomerId", CAST(s."InvoiceDate" AS date), 'Receipt', s."InvoiceNumber",
        'SaleReceipt', CAST(s."SaleId" AS bigint), 'Received against ' || s."InvoiceNumber",
        CAST(0 AS numeric(18,2)), CAST(s."ReceivedAmount" - COALESCE(a."Allocated", 0) AS numeric(18,2)), 2,
        CAST(s."SaleId" AS bigint), s."CreatedBy", CAST(s."CreatedAt" AS timestamp(3))
    FROM "Sales" s
    LEFT JOIN LATERAL (
        SELECT SUM(pa."AllocatedAmount") AS "Allocated"
        FROM "PaymentAllocations" pa
        WHERE pa."ReferenceType" = 'Sale' AND pa."ReferenceId" = s."SaleId"
    ) a ON true
    WHERE s."Status" = 'Posted' AND s."CustomerId" IS NOT NULL
      AND (s."ReceivedAmount" - COALESCE(a."Allocated", 0)) > 0
    UNION ALL
    SELECT p."CustomerId", CAST(p."PaymentDate" AS date), 'Payment Receipt', p."VoucherNumber",
        'Payment', CAST(p."PaymentId" AS bigint), 'Payment Receipt ' || p."VoucherNumber",
        CAST(0 AS numeric(18,2)), CAST(p."Amount" AS numeric(18,2)), 3,
        CAST(p."PaymentId" AS bigint), p."CreatedBy", CAST(p."CreatedAt" AS timestamp(3))
    FROM "Payments" p
    WHERE p."PartyType" = 'Customer' AND p."PaymentType" = 'Receipt'
      AND p."Status" = 'Posted' AND p."CustomerId" IS NOT NULL
    UNION ALL
    SELECT sr."CustomerId", CAST(sr."ReturnDate" AS date), 'Sales Return', sr."ReturnNumber",
        'SalesReturn', CAST(sr."SalesReturnId" AS bigint), 'Sales Return ' || sr."ReturnNumber",
        CAST(0 AS numeric(18,2)), CAST(sr."GrandTotal" - sr."RefundedAmount" AS numeric(18,2)), 4,
        CAST(sr."SalesReturnId" AS bigint), sr."CreatedBy", CAST(sr."CreatedAt" AS timestamp(3))
    FROM "SalesReturns" sr
    WHERE sr."Status" = 'Posted' AND sr."CustomerId" IS NOT NULL
      AND (sr."GrandTotal" - sr."RefundedAmount") > 0
),
ranked AS (
    SELECT e.*,
        SUM(e."Debit" - e."Credit") OVER (
            PARTITION BY e."CustomerId"
            ORDER BY e."TransactionDate", e."SortTime", e."TypeOrder", e."RefKey"
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) AS "RunningBalance",
        ROW_NUMBER() OVER (
            PARTITION BY e."CustomerId"
            ORDER BY e."TransactionDate", e."SortTime", e."TypeOrder", e."RefKey"
        ) AS "Seq"
    FROM entries e
)
SELECT r."CustomerId", r."Seq", r."TransactionDate", r."VoucherType", r."VoucherNumber",
       r."ReferenceType", r."ReferenceId", r."Narration", r."Debit", r."Credit",
       r."RunningBalance", r."CreatedBy", u."FullName" AS "CreatedByName"
FROM ranked r
LEFT JOIN "Users" u ON u."UserId" = r."CreatedBy";

/*-------------------------------- vw_CustomerOutstanding -------------------*/
CREATE VIEW "vw_CustomerOutstanding" AS
SELECT
    cu."CustomerId", cu."CustomerCode", cu."CustomerName", cu."Village", cu."Mobile",
    cu."CustomerType", cu."CreditLimit", cu."CreditDays", cu."IsActive",
    CAST(CASE WHEN cu."OpeningBalanceType" = 'DR' THEN cu."OpeningBalance" ELSE -cu."OpeningBalance" END AS numeric(18,2)) AS "OpeningBalance",
    COALESCE(inv."InvoiceCount", 0)     AS "InvoiceCount",
    COALESCE(inv."TotalBilled", 0)      AS "TotalBilled",
    COALESCE(inv."TotalReceived", 0)    AS "TotalReceived",
    COALESCE(inv."UnpaidBalance", 0)    AS "UnpaidBalance",
    COALESCE(ret."AdjustedReturns", 0)  AS "AdjustedReturns",
    COALESCE(adv."OnAccountAdvance", 0) AS "OnAccountAdvance",
    CAST(
        CASE WHEN cu."OpeningBalanceType" = 'DR' THEN cu."OpeningBalance" ELSE -cu."OpeningBalance" END
        + COALESCE(inv."UnpaidBalance", 0)
        - COALESCE(ret."AdjustedReturns", 0)
        - COALESCE(adv."OnAccountAdvance", 0)
    AS numeric(18,2)) AS "OutstandingAmount",
    inv."LastInvoiceDate", inv."OldestUnpaidDate",
    (CURRENT_DATE - inv."OldestUnpaidDate") AS "OldestUnpaidAgeDays"
FROM "Customers" AS cu
LEFT JOIN LATERAL (
    SELECT count(*) AS "InvoiceCount", SUM(s."GrandTotal") AS "TotalBilled",
           SUM(s."ReceivedAmount") AS "TotalReceived", SUM(s."BalanceAmount") AS "UnpaidBalance",
           MAX(s."InvoiceDate") AS "LastInvoiceDate",
           MIN(CASE WHEN s."BalanceAmount" > 0 THEN s."InvoiceDate" END) AS "OldestUnpaidDate"
    FROM "Sales" AS s
    WHERE s."CustomerId" = cu."CustomerId" AND s."Status" = 'Posted'
) AS inv ON true
LEFT JOIN LATERAL (
    SELECT SUM(sr."GrandTotal" - sr."RefundedAmount") AS "AdjustedReturns"
    FROM "SalesReturns" AS sr
    WHERE sr."CustomerId" = cu."CustomerId" AND sr."Status" = 'Posted'
) AS ret ON true
LEFT JOIN LATERAL (
    SELECT SUM(pm."UnallocatedAmount") AS "OnAccountAdvance"
    FROM "Payments" AS pm
    WHERE pm."CustomerId" = cu."CustomerId" AND pm."PartyType" = 'Customer'
      AND pm."PaymentType" = 'Receipt' AND pm."Status" = 'Posted'
) AS adv ON true
WHERE cu."IsDeleted" = false;

/*-------------------------------- vw_SupplierLedger -----------------------*/
CREATE VIEW "vw_SupplierLedger" AS
WITH entries AS (
    SELECT
        s."SupplierId",
        CAST(COALESCE(s."OpeningBalanceDate", CAST(s."CreatedAt" AS date)) AS date) AS "TransactionDate",
        'Opening Balance'                   AS "VoucherType",
        CAST(NULL AS varchar(30))           AS "VoucherNumber",
        'Opening'                           AS "ReferenceType",
        CAST(NULL AS bigint)                AS "ReferenceId",
        'Opening balance'                   AS "Narration",
        CAST(CASE WHEN s."OpeningBalanceType" = 'DR' THEN s."OpeningBalance" ELSE 0 END AS numeric(18,2)) AS "Debit",
        CAST(CASE WHEN s."OpeningBalanceType" = 'CR' THEN s."OpeningBalance" ELSE 0 END AS numeric(18,2)) AS "Credit",
        0                                   AS "TypeOrder",
        CAST(0 AS bigint)                   AS "RefKey",
        s."CreatedBy",
        CAST(s."CreatedAt" AS timestamp(3)) AS "SortTime"
    FROM "Suppliers" s
    WHERE s."IsDeleted" = false AND s."OpeningBalance" <> 0
    UNION ALL
    SELECT p."SupplierId", CAST(p."PurchaseDate" AS date), 'Purchase Bill', p."PurchaseNumber",
        'Purchase', CAST(p."PurchaseId" AS bigint), 'Purchase Bill ' || p."PurchaseNumber",
        CAST(0 AS numeric(18,2)), CAST(p."GrandTotal" AS numeric(18,2)), 1,
        CAST(p."PurchaseId" AS bigint), p."CreatedBy", CAST(p."CreatedAt" AS timestamp(3))
    FROM "Purchases" p
    WHERE p."Status" = 'Posted'
    UNION ALL
    SELECT p."SupplierId", CAST(p."PurchaseDate" AS date), 'Payment', p."PurchaseNumber",
        'PurchasePayment', CAST(p."PurchaseId" AS bigint), 'Paid against ' || p."PurchaseNumber",
        CAST(p."PaidAmount" - COALESCE(a."Allocated", 0) AS numeric(18,2)), CAST(0 AS numeric(18,2)), 2,
        CAST(p."PurchaseId" AS bigint), p."CreatedBy", CAST(p."CreatedAt" AS timestamp(3))
    FROM "Purchases" p
    LEFT JOIN LATERAL (
        SELECT SUM(pa."AllocatedAmount") AS "Allocated"
        FROM "PaymentAllocations" pa
        WHERE pa."ReferenceType" = 'Purchase' AND pa."ReferenceId" = p."PurchaseId"
    ) a ON true
    WHERE p."Status" = 'Posted' AND (p."PaidAmount" - COALESCE(a."Allocated", 0)) > 0
    UNION ALL
    SELECT pm."SupplierId", CAST(pm."PaymentDate" AS date), 'Payment Made', pm."VoucherNumber",
        'Payment', CAST(pm."PaymentId" AS bigint), 'Payment ' || pm."VoucherNumber",
        CAST(pm."Amount" AS numeric(18,2)), CAST(0 AS numeric(18,2)), 3,
        CAST(pm."PaymentId" AS bigint), pm."CreatedBy", CAST(pm."CreatedAt" AS timestamp(3))
    FROM "Payments" pm
    WHERE pm."PartyType" = 'Supplier' AND pm."PaymentType" = 'Payment'
      AND pm."Status" = 'Posted' AND pm."SupplierId" IS NOT NULL
    UNION ALL
    SELECT pr."SupplierId", CAST(pr."ReturnDate" AS date), 'Purchase Return', pr."ReturnNumber",
        'PurchaseReturn', CAST(pr."PurchaseReturnId" AS bigint), 'Purchase Return ' || pr."ReturnNumber",
        CAST(pr."GrandTotal" AS numeric(18,2)), CAST(0 AS numeric(18,2)), 4,
        CAST(pr."PurchaseReturnId" AS bigint), pr."CreatedBy", CAST(pr."CreatedAt" AS timestamp(3))
    FROM "PurchaseReturns" pr
    WHERE pr."Status" = 'Posted'
),
ranked AS (
    SELECT e.*,
        SUM(e."Debit" - e."Credit") OVER (
            PARTITION BY e."SupplierId"
            ORDER BY e."TransactionDate", e."SortTime", e."TypeOrder", e."RefKey"
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) AS "RunningBalance",
        ROW_NUMBER() OVER (
            PARTITION BY e."SupplierId"
            ORDER BY e."TransactionDate", e."SortTime", e."TypeOrder", e."RefKey"
        ) AS "Seq"
    FROM entries e
)
SELECT r."SupplierId", r."Seq", r."TransactionDate", r."VoucherType", r."VoucherNumber",
       r."ReferenceType", r."ReferenceId", r."Narration", r."Debit", r."Credit",
       r."RunningBalance", r."CreatedBy", u."FullName" AS "CreatedByName"
FROM ranked r
LEFT JOIN "Users" u ON u."UserId" = r."CreatedBy";

/*-------------------------------- vw_SupplierOutstanding ------------------*/
CREATE VIEW "vw_SupplierOutstanding" AS
SELECT
    su."SupplierId", su."SupplierCode", su."SupplierName", su."City", su."Phone",
    su."PaymentTermDays", su."CreditLimit", su."IsActive",
    CAST(CASE WHEN su."OpeningBalanceType" = 'CR' THEN su."OpeningBalance" ELSE -su."OpeningBalance" END AS numeric(18,2)) AS "OpeningBalance",
    COALESCE(pu."BillCount", 0)         AS "BillCount",
    COALESCE(pu."TotalPurchased", 0)    AS "TotalPurchased",
    COALESCE(pu."TotalPaid", 0)         AS "TotalPaid",
    COALESCE(pu."UnpaidBalance", 0)     AS "UnpaidBalance",
    COALESCE(pr."ReturnValue", 0)       AS "ReturnValue",
    COALESCE(adv."OnAccountAdvance", 0) AS "OnAccountAdvance",
    CAST(
        CASE WHEN su."OpeningBalanceType" = 'CR' THEN su."OpeningBalance" ELSE -su."OpeningBalance" END
        + COALESCE(pu."UnpaidBalance", 0)
        - COALESCE(pr."ReturnValue", 0)
        - COALESCE(adv."OnAccountAdvance", 0)
    AS numeric(18,2)) AS "OutstandingAmount",
    pu."LastPurchaseDate", pu."OldestUnpaidDate", pu."NextDueDate"
FROM "Suppliers" AS su
LEFT JOIN LATERAL (
    SELECT count(*) AS "BillCount", SUM(p."GrandTotal") AS "TotalPurchased",
           SUM(p."PaidAmount") AS "TotalPaid", SUM(p."BalanceAmount") AS "UnpaidBalance",
           MAX(p."PurchaseDate") AS "LastPurchaseDate",
           MIN(CASE WHEN p."BalanceAmount" > 0 THEN p."PurchaseDate" END) AS "OldestUnpaidDate",
           MIN(CASE WHEN p."BalanceAmount" > 0 THEN p."DueDate" END)      AS "NextDueDate"
    FROM "Purchases" AS p
    WHERE p."SupplierId" = su."SupplierId" AND p."Status" = 'Posted'
) AS pu ON true
LEFT JOIN LATERAL (
    SELECT SUM(r."GrandTotal") AS "ReturnValue"
    FROM "PurchaseReturns" AS r
    WHERE r."SupplierId" = su."SupplierId" AND r."Status" = 'Posted'
) AS pr ON true
LEFT JOIN LATERAL (
    SELECT SUM(pm."UnallocatedAmount") AS "OnAccountAdvance"
    FROM "Payments" AS pm
    WHERE pm."SupplierId" = su."SupplierId" AND pm."PartyType" = 'Supplier'
      AND pm."PaymentType" = 'Payment' AND pm."Status" = 'Posted'
) AS adv ON true
WHERE su."IsDeleted" = false;

/*-------------------------------- vw_DailySalesSummary --------------------*/
CREATE VIEW "vw_DailySalesSummary" AS
SELECT
    s."InvoiceDate",
    count(*) AS "InvoiceCount",
    CAST(SUM(s."TaxableAmount") AS numeric(18,2)) AS "TaxableAmount",
    CAST(SUM(s."CgstAmount" + s."SgstAmount" + s."IgstAmount" + s."CessAmount") AS numeric(18,2)) AS "TaxAmount",
    CAST(SUM(s."GrandTotal") AS numeric(18,2)) AS "TotalSales",
    CAST(SUM(s."TotalCostAmount") AS numeric(18,2)) AS "TotalCost",
    CAST(SUM(s."GrossProfit") AS numeric(18,2)) AS "GrossProfit",
    CAST(SUM(s."ReceivedAmount") AS numeric(18,2)) AS "AmountReceived",
    CAST(SUM(s."BalanceAmount") AS numeric(18,2)) AS "CreditGiven",
    SUM(CASE WHEN s."PaymentType" = 'Cash'   THEN 1 ELSE 0 END) AS "CashInvoiceCount",
    SUM(CASE WHEN s."PaymentType" = 'Credit' THEN 1 ELSE 0 END) AS "CreditInvoiceCount"
FROM "Sales" AS s
WHERE s."Status" = 'Posted'
GROUP BY s."InvoiceDate";

/*-------------------------------- vw_DailyPurchaseSummary -----------------*/
CREATE VIEW "vw_DailyPurchaseSummary" AS
SELECT
    p."PurchaseDate",
    count(*) AS "BillCount",
    CAST(SUM(p."TaxableAmount") AS numeric(18,2)) AS "TaxableAmount",
    CAST(SUM(p."CgstAmount" + p."SgstAmount" + p."IgstAmount" + p."CessAmount") AS numeric(18,2)) AS "TaxAmount",
    CAST(SUM(p."GrandTotal") AS numeric(18,2)) AS "TotalPurchase",
    CAST(SUM(p."PaidAmount") AS numeric(18,2)) AS "AmountPaid",
    CAST(SUM(p."BalanceAmount") AS numeric(18,2)) AS "AmountDue"
FROM "Purchases" AS p
WHERE p."Status" = 'Posted'
GROUP BY p."PurchaseDate";

/*-------------------------------- vw_ItemSubGroupWiseStock ----------------*/
CREATE VIEW "vw_ItemSubGroupWiseStock" AS
SELECT
    c."ItemSubGroupId", c."ItemSubGroupName", c."ParentItemSubGroupId",
    count(ps."ItemId") AS "ItemCount",
    SUM(CASE WHEN ps."CurrentStock" > 0  THEN 1 ELSE 0 END) AS "InStockCount",
    SUM(CASE WHEN ps."CurrentStock" <= 0 THEN 1 ELSE 0 END) AS "OutOfStockCount",
    SUM(CASE WHEN ps."StockStatus" = 'LowStock' THEN 1 ELSE 0 END) AS "LowStockCount",
    CAST(COALESCE(SUM(ps."CurrentStock"), 0)     AS numeric(18,3)) AS "TotalQuantity",
    CAST(COALESCE(SUM(ps."StockValueAtCost"), 0) AS numeric(18,2)) AS "StockValueAtCost",
    CAST(COALESCE(SUM(ps."StockValueAtMrp"), 0)  AS numeric(18,2)) AS "StockValueAtMrp"
FROM "ItemSubGroupMaster" AS c
LEFT JOIN "vw_ItemStock" AS ps ON ps."ItemSubGroupId" = c."ItemSubGroupId" AND ps."IsActive" = true
WHERE c."IsDeleted" = false
GROUP BY c."ItemSubGroupId", c."ItemSubGroupName", c."ParentItemSubGroupId";

/*-------------------------------- vw_CompanyWiseStock ---------------------*/
CREATE VIEW "vw_CompanyWiseStock" AS
SELECT
    co."CompanyId", co."CompanyName",
    count(ps."ItemId") AS "ItemCount",
    CAST(COALESCE(SUM(ps."CurrentStock"), 0)     AS numeric(18,3)) AS "TotalQuantity",
    CAST(COALESCE(SUM(ps."StockValueAtCost"), 0) AS numeric(18,2)) AS "StockValueAtCost",
    CAST(COALESCE(SUM(ps."StockValueAtMrp"), 0)  AS numeric(18,2)) AS "StockValueAtMrp"
FROM "Companies" AS co
LEFT JOIN "vw_ItemStock" AS ps ON ps."CompanyId" = co."CompanyId" AND ps."IsActive" = true
WHERE co."IsDeleted" = false
GROUP BY co."CompanyId", co."CompanyName";

/*-------------------------------- vw_LowStockItems ------------------------*/
CREATE VIEW "vw_LowStockItems" AS
SELECT ps.*,
    CASE WHEN ps."MaxStockLevel" > 0 THEN ps."MaxStockLevel" - ps."CurrentStock"
         ELSE ps."MinStockLevel" - ps."CurrentStock" END AS "SuggestedOrderQty"
FROM "vw_ItemStock" AS ps
WHERE ps."IsActive" = true AND ps."CurrentStock" > 0 AND ps."CurrentStock" <= ps."MinStockLevel";

/*-------------------------------- vw_OutOfStockItems ----------------------*/
CREATE VIEW "vw_OutOfStockItems" AS
SELECT ps.*
FROM "vw_ItemStock" AS ps
WHERE ps."IsActive" = true AND ps."CurrentStock" <= 0;

/*-------------------------------- vw_ExpiredStock -------------------------*/
CREATE VIEW "vw_ExpiredStock" AS
SELECT bs.*
FROM "vw_BatchStock" AS bs
WHERE bs."CurrentQty" > 0 AND bs."ExpiryStatus" = 'Expired';

/*-------------------------------- vw_NearExpiryStock ----------------------*/
CREATE VIEW "vw_NearExpiryStock" AS
SELECT bs.*
FROM "vw_BatchStock" AS bs
WHERE bs."CurrentQty" > 0 AND bs."ExpiryStatus" IN ('Critical', 'Warning');

/*-------------------------------- vw_GstSalesSummary ----------------------*/
CREATE VIEW "vw_GstSalesSummary" AS
SELECT
    s."InvoiceDate", sd."HsnCode", sd."GstPercent", s."IsInterState",
    count(DISTINCT s."SaleId") AS "InvoiceCount",
    CAST(SUM(sd."TotalQuantity") AS numeric(18,3)) AS "TotalQuantity",
    CAST(SUM(sd."TaxableAmount") AS numeric(18,2)) AS "TaxableAmount",
    CAST(SUM(sd."CgstAmount") AS numeric(18,2)) AS "CgstAmount",
    CAST(SUM(sd."SgstAmount") AS numeric(18,2)) AS "SgstAmount",
    CAST(SUM(sd."IgstAmount") AS numeric(18,2)) AS "IgstAmount",
    CAST(SUM(sd."CessAmount") AS numeric(18,2)) AS "CessAmount",
    CAST(SUM(sd."LineTotal") AS numeric(18,2)) AS "TotalAmount"
FROM "Sales" AS s
INNER JOIN "SalesDetails" AS sd ON sd."SaleId" = s."SaleId"
WHERE s."Status" = 'Posted'
GROUP BY s."InvoiceDate", sd."HsnCode", sd."GstPercent", s."IsInterState";

/*-------------------------------- vw_GstPurchaseSummary -------------------*/
CREATE VIEW "vw_GstPurchaseSummary" AS
SELECT
    p."PurchaseDate", pd."HsnCode", pd."GstPercent", p."IsInterState",
    count(DISTINCT p."PurchaseId") AS "BillCount",
    CAST(SUM(pd."TotalQuantity") AS numeric(18,3)) AS "TotalQuantity",
    CAST(SUM(pd."TaxableAmount") AS numeric(18,2)) AS "TaxableAmount",
    CAST(SUM(pd."CgstAmount") AS numeric(18,2)) AS "CgstAmount",
    CAST(SUM(pd."SgstAmount") AS numeric(18,2)) AS "SgstAmount",
    CAST(SUM(pd."IgstAmount") AS numeric(18,2)) AS "IgstAmount",
    CAST(SUM(pd."CessAmount") AS numeric(18,2)) AS "CessAmount",
    CAST(SUM(pd."LineTotal") AS numeric(18,2)) AS "TotalAmount"
FROM "Purchases" AS p
INNER JOIN "PurchaseDetails" AS pd ON pd."PurchaseId" = p."PurchaseId"
WHERE p."Status" = 'Posted'
GROUP BY p."PurchaseDate", pd."HsnCode", pd."GstPercent", p."IsInterState";
