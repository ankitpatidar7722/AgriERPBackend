/* ============================================================================
   31_PurchaseOrderPacks.sql

   Purchase Order lines gain a pack breakdown and a second remark.

   The buyer now enters goods in packs: No. of Packs x Qty per Pack gives the
   P.O. quantity (kept in the existing OrderedQty column - it IS the ordered
   qty, so no new computed column is needed; Total Amount stays EstimatedAmount
   = OrderedQty * Rate). RequiredQty is the figure copied from the requisition
   line the PO was raised from, held as a snapshot so it still shows after the
   requisition moves on. ItemRemark is a per-line note distinct from Remarks.

   Idempotent: safe to re-run. Follows 22/30's ALTER-if-absent pattern.
   ============================================================================ */

-- Required because PurchaseOrderDetails carries PERSISTED computed columns
-- (PendingQty, EstimatedAmount); ALTER TABLE refuses without these set.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('PurchaseOrderDetails', 'NoOfPacks') IS NULL
    ALTER TABLE PurchaseOrderDetails
        ADD NoOfPacks DECIMAL(18,3) NOT NULL
            CONSTRAINT DF_PurchaseOrderDetails_NoOfPacks DEFAULT (0);
GO

IF COL_LENGTH('PurchaseOrderDetails', 'QtyPerPack') IS NULL
    ALTER TABLE PurchaseOrderDetails
        ADD QtyPerPack DECIMAL(18,3) NOT NULL
            CONSTRAINT DF_PurchaseOrderDetails_QtyPerPack DEFAULT (0);
GO

-- Snapshot of the requisition line's required quantity; NULL for a direct order.
IF COL_LENGTH('PurchaseOrderDetails', 'RequiredQty') IS NULL
    ALTER TABLE PurchaseOrderDetails ADD RequiredQty DECIMAL(18,3) NULL;
GO

-- A note about the item on this order, kept apart from the line's own Remarks.
IF COL_LENGTH('PurchaseOrderDetails', 'ItemRemark') IS NULL
    ALTER TABLE PurchaseOrderDetails ADD ItemRemark NVARCHAR(300) NULL;
GO
