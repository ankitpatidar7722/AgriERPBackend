/*==============================================================================
  AgriERP  |  22_PurchaseRequisitions.sql
  ------------------------------------------------------------------------------
  The Purchase Requisition - the first step of procurement, the "what to buy"
  document that comes before the Purchase Order:

      Purchase Requisition (REQ) -> Purchase Order (PO) -> Purchase GRN (PGRN)

  A requisition lists items + required quantity. It carries NO supplier - the
  supplier is chosen on the PO. A PO is raised "from a requisition": it pulls the
  requisition's pending lines and, as it does, adds to OrderedQty on each
  requisition line. PendingQty (RequiredQty - OrderedQty) then drives the status
  Open -> Partial -> Converted, exactly the way a PO's PendingQty drives
  Open -> Partial -> Received.

  Idempotent: safe to re-run. Self-checks at the end.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* PurchaseRequisitions (header)                                              */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'PurchaseRequisitions', N'U') IS NULL
BEGIN
    CREATE TABLE PurchaseRequisitions
    (
        RequisitionId       BIGINT          IDENTITY(1,1) NOT NULL,
        RequisitionNumber   NVARCHAR(30)    NOT NULL,
        RequisitionDate     DATE            NOT NULL,
        LocationId          INT             NOT NULL,
        Remarks             NVARCHAR(500)   NULL,

        TotalQty            DECIMAL(18,3)   NOT NULL CONSTRAINT DF_PurchaseRequisitions_TotalQty DEFAULT (0),

        -- Open -> Partial -> Converted, or Cancelled. Derived from how much of
        -- the requisition has been pulled into purchase orders.
        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_PurchaseRequisitions_Status DEFAULT ('Open'),

        VoucherId           INT             NULL,
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_PurchaseRequisitions_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_PurchaseRequisitions PRIMARY KEY CLUSTERED (RequisitionId),
        CONSTRAINT UQ_PurchaseRequisitions_Number UNIQUE (RequisitionNumber),
        CONSTRAINT FK_PurchaseRequisitions_Location
            FOREIGN KEY (LocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT FK_PurchaseRequisitions_Voucher
            FOREIGN KEY (VoucherId) REFERENCES VoucherMaster (VoucherId),
        CONSTRAINT CK_PurchaseRequisitions_Status
            CHECK (Status IN ('Draft','Open','Partial','Converted','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_PurchaseRequisitions_Status
        ON PurchaseRequisitions (Status, RequisitionDate DESC) INCLUDE (RequisitionNumber);
END
GO

/*----------------------------------------------------------------------------*/
/* PurchaseRequisitionDetails (lines)                                         */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'PurchaseRequisitionDetails', N'U') IS NULL
BEGIN
    CREATE TABLE PurchaseRequisitionDetails
    (
        RequisitionDetailId BIGINT          IDENTITY(1,1) NOT NULL,
        RequisitionId       BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ItemId              INT             NOT NULL,
        RequiredQty         DECIMAL(18,3)   NOT NULL,
        -- Maintained as purchase orders consume the line; RequiredQty-OrderedQty
        -- is what is still to be ordered, which is how the status is derived.
        OrderedQty          DECIMAL(18,3)   NOT NULL CONSTRAINT DF_PurchaseRequisitionDetails_OrderedQty DEFAULT (0),
        PendingQty          AS (RequiredQty - OrderedQty) PERSISTED,
        UnitId              INT             NOT NULL,
        ExpectedDate        DATE            NULL,
        EstimatedRate       DECIMAL(18,4)   NOT NULL CONSTRAINT DF_PurchaseRequisitionDetails_Rate DEFAULT (0),
        Remarks             NVARCHAR(300)   NULL,

        CONSTRAINT PK_PurchaseRequisitionDetails PRIMARY KEY CLUSTERED (RequisitionDetailId),
        CONSTRAINT FK_PurchaseRequisitionDetails_Req
            FOREIGN KEY (RequisitionId) REFERENCES PurchaseRequisitions (RequisitionId) ON DELETE CASCADE,
        CONSTRAINT FK_PurchaseRequisitionDetails_Item
            FOREIGN KEY (ItemId) REFERENCES ItemMaster (ItemId),
        CONSTRAINT FK_PurchaseRequisitionDetails_Unit
            FOREIGN KEY (UnitId) REFERENCES Units (UnitId),
        CONSTRAINT CK_PurchaseRequisitionDetails_Qty
            CHECK (RequiredQty > 0 AND OrderedQty >= 0 AND OrderedQty <= RequiredQty)
    );

    CREATE NONCLUSTERED INDEX IX_PurchaseRequisitionDetails_ReqId
        ON PurchaseRequisitionDetails (RequisitionId, LineNumber);
    CREATE NONCLUSTERED INDEX IX_PurchaseRequisitionDetails_ItemId
        ON PurchaseRequisitionDetails (ItemId) INCLUDE (RequisitionId, PendingQty);
END
GO

/*----------------------------------------------------------------------------*/
/* Link a PO line back to the requisition line it fulfils                     */
/*----------------------------------------------------------------------------*/
IF COL_LENGTH('PurchaseOrderDetails', 'RequisitionDetailId') IS NULL
    ALTER TABLE PurchaseOrderDetails ADD RequisitionDetailId BIGINT NULL;
GO
IF OBJECT_ID(N'FK_PurchaseOrderDetails_Requisition', N'F') IS NULL
    ALTER TABLE PurchaseOrderDetails ADD CONSTRAINT FK_PurchaseOrderDetails_Requisition
        FOREIGN KEY (RequisitionDetailId) REFERENCES PurchaseRequisitionDetails (RequisitionDetailId);
GO

/*----------------------------------------------------------------------------*/
/* PREQ voucher (sorts before PO=10 / PGRN=20)                                */
/*----------------------------------------------------------------------------*/
MERGE VoucherMaster AS target
USING (VALUES
    ('PREQ', N'Purchase Requisition', N'PurchaseRequisition', N'REQ', 5)
) AS source (VoucherCode, VoucherName, VoucherType, Prefix, DisplayOrder)
    ON target.VoucherCode = source.VoucherCode
WHEN MATCHED THEN
    UPDATE SET VoucherName = source.VoucherName, VoucherType = source.VoucherType,
               Prefix = source.Prefix, DisplayOrder = source.DisplayOrder
WHEN NOT MATCHED THEN
    INSERT (VoucherCode, VoucherName, VoucherType, Prefix, DisplayOrder)
    VALUES (source.VoucherCode, source.VoucherName, source.VoucherType, source.Prefix, source.DisplayOrder);
GO

/*----------------------------------------------------------------------------*/
/* REQ number series for the active financial year (reads REQ/2026-27/00001)  */
/*----------------------------------------------------------------------------*/
DECLARE @ActiveFy INT = (SELECT FinancialYearId FROM FinancialYears WHERE IsActive = 1);
IF @ActiveFy IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM NumberSeries WHERE DocumentType = N'PurchaseRequisition' AND FinancialYearId = @ActiveFy)
    INSERT INTO NumberSeries (DocumentType, FinancialYearId, Prefix, PaddingLength, IncludeYearCode, Separator)
    VALUES (N'PurchaseRequisition', @ActiveFy, N'REQ', 5, 1, N'/');
GO

/*----------------------------------------------------------------------------*/
/* self-check                                                                 */
/*----------------------------------------------------------------------------*/
DECLARE @tables INT =
    (SELECT COUNT(*) FROM sys.tables WHERE name IN ('PurchaseRequisitions', 'PurchaseRequisitionDetails'));
DECLARE @voucher INT = (SELECT COUNT(*) FROM VoucherMaster WHERE VoucherCode = 'PREQ');
DECLARE @link INT = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('PurchaseOrderDetails') AND name = 'RequisitionDetailId');
DECLARE @series INT = (SELECT COUNT(*) FROM NumberSeries WHERE DocumentType = N'PurchaseRequisition');

IF @tables = 2 AND @voucher = 1 AND @link = 1 AND @series >= 1
    PRINT N'22_PurchaseRequisitions.sql completed. Requisition tables + PREQ voucher + PO link + REQ number series in place.';
ELSE
    PRINT N'22_PurchaseRequisitions.sql WARNING: tables=' + CAST(@tables AS NVARCHAR(10))
        + N', voucher=' + CAST(@voucher AS NVARCHAR(10))
        + N', link=' + CAST(@link AS NVARCHAR(10))
        + N', series=' + CAST(@series AS NVARCHAR(10));
GO
