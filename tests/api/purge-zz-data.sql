-- =============================================================================
--  Removes every ZZ-prefixed test fixture.
--
--  Shared by transactions-smoke.ps1 and seed-demo-data.ps1 so there is exactly
--  one definition of "what a test leaves behind". Two copies drift, and the
--  half that forgets a child table leaves an orphan that fails the NEXT run
--  with a duplicate-key error pointing nowhere near the cause.
--
--  Ordered child-to-parent so no foreign key blocks a delete. Product price
--  history was the table that bit; it has no obvious link to a "sale" but
--  holds a row for every rate change.
--
--  Safe to run against a database holding real data: everything is matched on
--  the ZZ name prefix, which no genuine record uses.
--
--  Requires sqlcmd -I (QUOTED_IDENTIFIER ON) - the filtered indexes reject
--  any DML without it, with Msg 1934.
-- =============================================================================
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Sale ids are captured FIRST. The deletes below remove sales detail rows
-- before the headers, so any subquery that looks for them at header-delete
-- time would find nothing and leave orphan headers behind.
IF OBJECT_ID('tempdb..#ZZSales') IS NOT NULL DROP TABLE #ZZSales;
SELECT DISTINCT s.SaleId INTO #ZZSales
FROM Sales s
WHERE s.CustomerId IN (SELECT CustomerId FROM Customers WHERE CustomerName LIKE 'ZZ%')
   OR s.WalkInCustomerName LIKE 'ZZ%'
   OR EXISTS (SELECT 1 FROM SalesDetails sd INNER JOIN ItemMaster p ON p.ItemId = sd.ItemId
              WHERE sd.SaleId = s.SaleId AND p.ItemName LIKE 'ZZ%');

DELETE FROM PaymentAllocations WHERE PaymentId IN (SELECT PaymentId FROM Payments WHERE CustomerId IN (SELECT CustomerId FROM Customers WHERE CustomerName LIKE 'ZZ%') OR SupplierId IN (SELECT SupplierId FROM Suppliers WHERE SupplierName LIKE 'ZZ%'));
DELETE FROM Payments WHERE CustomerId IN (SELECT CustomerId FROM Customers WHERE CustomerName LIKE 'ZZ%') OR SupplierId IN (SELECT SupplierId FROM Suppliers WHERE SupplierName LIKE 'ZZ%');
DELETE srd FROM SalesReturnDetails srd INNER JOIN ItemMaster p ON p.ItemId = srd.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE FROM SalesReturns WHERE CustomerId IN (SELECT CustomerId FROM Customers WHERE CustomerName LIKE 'ZZ%');
DELETE FROM SalePayments WHERE SaleId IN (SELECT SaleId FROM Sales WHERE CustomerId IN (SELECT CustomerId FROM Customers WHERE CustomerName LIKE 'ZZ%') OR WalkInCustomerName LIKE 'ZZ%');
DELETE sd FROM SalesDetails sd INNER JOIN ItemMaster p ON p.ItemId = sd.ItemId WHERE p.ItemName LIKE 'ZZ%';
-- Uses the ids captured before the detail rows were removed. A bill raised
-- through the UI as an unnamed walk-in matches on its products, not its party.
DELETE FROM SalePayments WHERE SaleId IN (SELECT SaleId FROM #ZZSales);
DELETE FROM SalesDetails WHERE SaleId IN (SELECT SaleId FROM #ZZSales);
DELETE FROM Sales WHERE SaleId IN (SELECT SaleId FROM #ZZSales);
DELETE prd FROM PurchaseReturnDetails prd INNER JOIN ItemMaster p ON p.ItemId = prd.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE FROM PurchaseReturns WHERE SupplierId IN (SELECT SupplierId FROM Suppliers WHERE SupplierName LIKE 'ZZ%');
DELETE pd FROM PurchaseDetails pd INNER JOIN ItemMaster p ON p.ItemId = pd.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE FROM Purchases WHERE SupplierId IN (SELECT SupplierId FROM Suppliers WHERE SupplierName LIKE 'ZZ%');
DELETE pod FROM PurchaseOrderDetails pod INNER JOIN ItemMaster p ON p.ItemId = pod.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE FROM PurchaseOrders WHERE SupplierId IN (SELECT SupplierId FROM Suppliers WHERE SupplierName LIKE 'ZZ%');
DELETE ad FROM StockAdjustmentDetails ad INNER JOIN ItemMaster p ON p.ItemId = ad.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE FROM StockAdjustments WHERE NOT EXISTS (SELECT 1 FROM StockAdjustmentDetails d WHERE d.AdjustmentId = StockAdjustments.AdjustmentId);
DELETE td FROM StockTransferDetails td INNER JOIN ItemMaster p ON p.ItemId = td.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE st FROM StockTransactions st INNER JOIN ItemMaster p ON p.ItemId = st.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE FROM ItemPriceHistory WHERE ItemId IN (SELECT ItemId FROM ItemMaster WHERE ItemName LIKE 'ZZ%');
DELETE FROM ItemImages WHERE ItemId IN (SELECT ItemId FROM ItemMaster WHERE ItemName LIKE 'ZZ%');
DELETE b FROM ItemBatches b INNER JOIN ItemMaster p ON p.ItemId = b.ItemId WHERE p.ItemName LIKE 'ZZ%';
DELETE FROM ItemMaster   WHERE ItemName  LIKE 'ZZ%';
DELETE FROM Customers  WHERE CustomerName LIKE 'ZZ%';
DELETE FROM Suppliers  WHERE SupplierName LIKE 'ZZ%';

-- Masters that api-smoke.ps1 creates. It purges them itself at the end, but
-- "at the end" is exactly what a killed or aborted run never reaches - and a
-- leftover ZZ category makes the NEXT run's Reference_data_is_seeded fail on a
-- count, which points nowhere near the actual cause. These run after Products
-- so nothing still references them.
DELETE FROM ItemSubGroupMaster WHERE ItemSubGroupCode LIKE 'ZZ%' OR ItemSubGroupName LIKE 'ZZ%';
DELETE FROM Companies  WHERE CompanyCode  LIKE 'ZZ%' OR CompanyName  LIKE 'ZZ%';
DELETE FROM Units      WHERE UnitCode     LIKE 'ZZ%' OR UnitName     LIKE 'ZZ%';

-- =============================================================================
--  Number series: reset ONLY where nothing of that kind survives.
--
--  This used to be a blanket `SET CurrentNumber = 0`, which is wrong and was
--  actively harmful. The purge deletes ZZ-prefixed rows only, so anything the
--  shop entered by hand stays - and rewinding the counter past it makes the
--  next save re-issue a code that already exists. It surfaced as a 500 from
--  SQL Server:
--
--      Cannot insert duplicate key row in object 'ItemMaster' with unique
--      index 'UQ_Products_ItemCode'. The duplicate key value is (PRD-000007).
--
--  On a real database the same bug would re-issue an INVOICE number, which is
--  a GST problem rather than an inconvenience.
--
--  The counter only ever moves forward, so leaving it alone when rows remain is
--  always safe: it is already at or above the highest number in use. Gaps are
--  fine; collisions are not.
-- =============================================================================
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'Product'         AND NOT EXISTS (SELECT 1 FROM ItemMaster);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'Customer'        AND NOT EXISTS (SELECT 1 FROM Customers);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'Supplier'        AND NOT EXISTS (SELECT 1 FROM Suppliers);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'Sale'            AND NOT EXISTS (SELECT 1 FROM Sales);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'Purchase'        AND NOT EXISTS (SELECT 1 FROM Purchases);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'SalesReturn'     AND NOT EXISTS (SELECT 1 FROM SalesReturns);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'PurchaseReturn'  AND NOT EXISTS (SELECT 1 FROM PurchaseReturns);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'PurchaseOrder'   AND NOT EXISTS (SELECT 1 FROM PurchaseOrders);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'StockAdjustment' AND NOT EXISTS (SELECT 1 FROM StockAdjustments);
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType = 'StockTransfer'   AND NOT EXISTS (SELECT 1 FROM StockTransfers);
-- Receipts and payments share one table.
UPDATE NumberSeries SET CurrentNumber = 0
WHERE DocumentType IN ('Receipt', 'Payment') AND NOT EXISTS (SELECT 1 FROM Payments);
