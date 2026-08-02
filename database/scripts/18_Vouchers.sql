/*==============================================================================
  AgriERP  |  18_Vouchers.sql
  ------------------------------------------------------------------------------
  The voucher registry - the Indus-style document-type table.

  One transaction id already ties a document's header to its lines
  (Purchases.PurchaseId, PurchaseOrders.PurchaseOrderId). VoucherId now
  says WHICH document it is - a Purchase Order (booking) or a Purchase GRN (goods
  received, stock in) - so the same purchase tables carry both, distinguished by
  voucher, exactly the way ItemTransactionMain does in Indus.

  Option B: the GRN is the goods-receipt document and there is no separate
  purchase invoice, so every existing Purchases row is a GRN and is
  backfilled to the GRN voucher. A GRN may reference a PO or stand alone
  (PurchaseOrderId is already nullable), so direct buys still work.

  Idempotent: safe to re-run. Self-checks at the end.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* VoucherMaster                                                           */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'VoucherMaster', N'U') IS NULL
BEGIN
    CREATE TABLE VoucherMaster
    (
        VoucherId       INT             IDENTITY(1,1) NOT NULL,
        -- Stable key the code refers to; never renumbered.
        VoucherCode     NVARCHAR(20)    NOT NULL,   -- 'PO', 'PGRN', ...
        VoucherName     NVARCHAR(60)    NOT NULL,   -- shown to users: 'Purchase GRN'
        -- Family the document belongs to, so one query can pull "all purchase
        -- vouchers" without hard-coding ids.
        VoucherType     NVARCHAR(30)    NOT NULL,   -- 'PurchaseOrder', 'Purchase', 'Sales'
        Prefix          NVARCHAR(15)    NOT NULL,   -- document-number prefix: 'PO', 'GRN'
        DisplayOrder    INT             NOT NULL CONSTRAINT DF_VoucherMaster_Order  DEFAULT (0),
        IsActive        BIT             NOT NULL CONSTRAINT DF_VoucherMaster_Active DEFAULT (1),
        CreatedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_VoucherMaster_CreatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_VoucherMaster PRIMARY KEY CLUSTERED (VoucherId),
        CONSTRAINT UQ_VoucherMaster_Code UNIQUE (VoucherCode)
    );
END
GO

/* Seed / refresh the vouchers - idempotent by VoucherCode. */
MERGE VoucherMaster AS target
USING (VALUES
    ('PO',   N'Purchase Order', N'PurchaseOrder', N'PO',  10),
    ('PGRN', N'Purchase GRN',   N'Purchase',      N'GRN', 20)
) AS source (VoucherCode, VoucherName, VoucherType, Prefix, DisplayOrder)
    ON target.VoucherCode = source.VoucherCode
WHEN MATCHED THEN
    UPDATE SET VoucherName  = source.VoucherName,
               VoucherType  = source.VoucherType,
               Prefix       = source.Prefix,
               DisplayOrder = source.DisplayOrder
WHEN NOT MATCHED THEN
    INSERT (VoucherCode, VoucherName, VoucherType, Prefix, DisplayOrder)
    VALUES (source.VoucherCode, source.VoucherName, source.VoucherType, source.Prefix, source.DisplayOrder);
GO

/*----------------------------------------------------------------------------*/
/* VoucherId on the purchase documents                                        */
/*----------------------------------------------------------------------------*/
IF COL_LENGTH('PurchaseOrders', 'VoucherId') IS NULL
    ALTER TABLE PurchaseOrders ADD VoucherId INT NULL;
GO
IF COL_LENGTH('Purchases', 'VoucherId') IS NULL
    ALTER TABLE Purchases ADD VoucherId INT NULL;
GO

IF OBJECT_ID(N'FK_PurchaseOrders_Voucher', N'F') IS NULL
    ALTER TABLE PurchaseOrders ADD CONSTRAINT FK_PurchaseOrders_Voucher
        FOREIGN KEY (VoucherId) REFERENCES VoucherMaster (VoucherId);
GO
IF OBJECT_ID(N'FK_Purchases_Voucher', N'F') IS NULL
    ALTER TABLE Purchases ADD CONSTRAINT FK_Purchases_Voucher
        FOREIGN KEY (VoucherId) REFERENCES VoucherMaster (VoucherId);
GO

/* Backfill: every existing order is a PO, every existing purchase is a GRN. */
DECLARE @po  INT = (SELECT VoucherId FROM VoucherMaster WHERE VoucherCode = 'PO');
DECLARE @grn INT = (SELECT VoucherId FROM VoucherMaster WHERE VoucherCode = 'PGRN');

UPDATE PurchaseOrders SET VoucherId = @po  WHERE VoucherId IS NULL;
UPDATE Purchases      SET VoucherId = @grn WHERE VoucherId IS NULL;
GO

/*----------------------------------------------------------------------------*/
/* self-check                                                                  */
/*----------------------------------------------------------------------------*/
DECLARE @vouchers INT = (SELECT COUNT(*) FROM VoucherMaster);
DECLARE @poNull   INT = (SELECT COUNT(*) FROM PurchaseOrders WHERE VoucherId IS NULL);
DECLARE @grnNull  INT = (SELECT COUNT(*) FROM Purchases WHERE VoucherId IS NULL);

IF @vouchers >= 2 AND @poNull = 0 AND @grnNull = 0
    PRINT N'18_Vouchers.sql completed. RESULT: ' + CAST(@vouchers AS NVARCHAR(10))
        + N' vouchers, all purchase documents tagged.';
ELSE
    PRINT N'18_Vouchers.sql WARNING: vouchers=' + CAST(@vouchers AS NVARCHAR(10))
        + N', PO-null=' + CAST(@poNull AS NVARCHAR(10))
        + N', GRN-null=' + CAST(@grnNull AS NVARCHAR(10));
GO
