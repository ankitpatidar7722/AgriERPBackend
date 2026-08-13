/* =====================================================================
   15_GrnOrderLineLink.sql  (SQL Server)

   Adds per-line PO linkage to GRN lines (PurchaseDetails) so a single GRN
   can receive against several purchase orders of the same supplier and
   reconcile each received line back to its own PO line.

   Idempotent: safe to run on an existing database more than once.
   The base install (06_Purchase.sql) already includes this column for
   fresh databases; this script upgrades databases created before it.
   ===================================================================== */

IF COL_LENGTH(N'PurchaseDetails', N'PurchaseOrderDetailId') IS NULL
BEGIN
    ALTER TABLE PurchaseDetails ADD PurchaseOrderDetailId BIGINT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PurchaseDetails_OrderDetail')
BEGIN
    ALTER TABLE PurchaseDetails ADD CONSTRAINT FK_PurchaseDetails_OrderDetail
        FOREIGN KEY (PurchaseOrderDetailId) REFERENCES PurchaseOrderDetails (PurchaseOrderDetailId);
END
GO
