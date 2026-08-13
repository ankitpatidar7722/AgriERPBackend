/* =====================================================================
   14_grn_order_line_link.sql  (PostgreSQL)

   Adds per-line PO linkage to GRN lines ("PurchaseDetails") so a single GRN
   can receive against several purchase orders of the same supplier and
   reconcile each received line back to its own PO line.

   Idempotent: safe to run on an existing database more than once.
   The base install (06_purchase.sql + 80_foreign_keys.sql) already includes
   this for fresh databases; this script upgrades databases created before it.
   ===================================================================== */

ALTER TABLE "PurchaseDetails" ADD COLUMN IF NOT EXISTS "PurchaseOrderDetailId" bigint;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_PurchaseDetails_OrderDetail'
    ) THEN
        ALTER TABLE "PurchaseDetails" ADD CONSTRAINT "FK_PurchaseDetails_OrderDetail"
            FOREIGN KEY ("PurchaseOrderDetailId") REFERENCES "PurchaseOrderDetails" ("PurchaseOrderDetailId");
    END IF;
END $$;
