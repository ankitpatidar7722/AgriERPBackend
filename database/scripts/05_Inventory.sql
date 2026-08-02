/*==============================================================================
  AgriERP  |  05_Inventory.sql
  ------------------------------------------------------------------------------
  The stock journal, plus adjustment and transfer documents.

  THE ONE RULE OF THIS SCHEMA
  ---------------------------
  StockTransactions is an APPEND-ONLY journal. Every movement of every
  batch - purchase, sale, return, adjustment, transfer, opening - is one row
  here, and nothing ever updates or deletes a row. Cancelling a bill writes a
  reversing row; it does not erase history.

  That single rule is what makes the rest of the system trustworthy:

      * Stock at any past date = SUM(SignedQuantity) up to that date. Closing
        stock for 31-March is reproducible three years later.
      * ProductBatches.CurrentQty is a running cache of this journal, and
        usp_RebuildBatchQuantities can rebuild it from scratch to prove the
        cache is honest.
      * "Who reduced this stock and when" is always answerable, which matters
        when the counter and the godown disagree.

  You asked for both StockLedger and StockTransactions. Two tables holding the
  same movements would be duplicate data - the thing you explicitly asked to
  avoid - so the journal is the table and the ledger (with running balance) is
  vw_StockLedger in 10_Views.sql. Same report, no second copy to reconcile.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* TransactionTypes  - closed lookup, ids are referenced by procedures      */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'TransactionTypes', N'U') IS NULL
BEGIN
    CREATE TABLE TransactionTypes
    (
        TransactionTypeId   TINYINT         NOT NULL,
        TypeCode            NVARCHAR(30)    NOT NULL,
        TypeName            NVARCHAR(60)    NOT NULL,
        Direction           SMALLINT        NOT NULL,   -- +1 inward, -1 outward
        IsActive            BIT             NOT NULL CONSTRAINT DF_TransactionTypes_IsActive DEFAULT (1),
        CONSTRAINT PK_TransactionTypes PRIMARY KEY CLUSTERED (TransactionTypeId),
        CONSTRAINT UQ_TransactionTypes_Code UNIQUE (TypeCode),
        CONSTRAINT CK_TransactionTypes_Direction CHECK (Direction IN (-1, 1))
    );
END
GO

-- Seeded here rather than in 12_SeedData.sql: these ids are a structural enum
-- that stored procedures and C# code depend on, not user-editable data.
MERGE TransactionTypes AS tgt
USING (VALUES
    ( 1, N'OpeningStock',       N'Opening Stock',           1),
    ( 2, N'PurchaseIn',         N'Purchase',                1),
    ( 3, N'PurchaseReturnOut',  N'Purchase Return',        -1),
    ( 4, N'SalesOut',           N'Sale',                   -1),
    ( 5, N'SalesReturnIn',      N'Sales Return',            1),
    ( 6, N'AdjustmentIn',       N'Adjustment (Increase)',   1),
    ( 7, N'AdjustmentOut',      N'Adjustment (Decrease)',  -1),
    ( 8, N'TransferOut',        N'Transfer Out',           -1),
    ( 9, N'TransferIn',         N'Transfer In',             1),
    (10, N'ExpiryWriteOff',     N'Expiry Write-Off',       -1),
    (11, N'DamageWriteOff',     N'Damage Write-Off',       -1)
) AS src (TransactionTypeId, TypeCode, TypeName, Direction)
    ON tgt.TransactionTypeId = src.TransactionTypeId
WHEN MATCHED THEN UPDATE SET tgt.TypeCode = src.TypeCode, tgt.TypeName = src.TypeName, tgt.Direction = src.Direction
WHEN NOT MATCHED BY TARGET THEN
    INSERT (TransactionTypeId, TypeCode, TypeName, Direction)
    VALUES (src.TransactionTypeId, src.TypeCode, src.TypeName, src.Direction);
GO

/*----------------------------------------------------------------------------*/
/* StockTransactions  - append-only movement journal                       */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'StockTransactions', N'U') IS NULL
BEGIN
    CREATE TABLE StockTransactions
    (
        StockTransactionId  BIGINT          IDENTITY(1,1) NOT NULL,
        TransactionDate     DATETIME2(3)    NOT NULL,
        TransactionTypeId   TINYINT         NOT NULL,

        ProductId           INT             NOT NULL,
        BatchId             BIGINT          NOT NULL,
        LocationId          INT             NOT NULL,

        -- Direction is duplicated from TransactionTypes on purpose: a PERSISTED
        -- computed column cannot read another table, and SignedQuantity must be
        -- persisted so date-range stock sums are a pure index scan.
        -- CK_StockTransactions_Direction keeps the two in step.
        Direction           SMALLINT        NOT NULL,
        Quantity            DECIMAL(18,3)   NOT NULL,               -- always positive
        SignedQuantity      AS (Quantity * Direction) PERSISTED,

        Rate                DECIMAL(18,4)   NOT NULL CONSTRAINT DF_StockTransactions_Rate DEFAULT (0),
        Value               AS (CAST(Quantity * Rate AS DECIMAL(18,2))) PERSISTED,

        /* Polymorphic link to the source document. Deliberately NOT a foreign
           key: one journal serves six document types, and six nullable FK
           columns would be worse in every way that matters. */
        ReferenceType       NVARCHAR(30)    NULL,   -- 'Purchase','Sale','Adjustment',...
        ReferenceId         BIGINT          NULL,   -- header id
        ReferenceDetailId   BIGINT          NULL,   -- line id
        ReferenceNumber     NVARCHAR(30)    NULL,   -- human-readable doc number

        -- Set on the reversing row when a document is cancelled.
        ReversesTransactionId BIGINT        NULL,

        Remarks             NVARCHAR(300)   NULL,
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_StockTransactions_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,

        CONSTRAINT PK_StockTransactions PRIMARY KEY CLUSTERED (StockTransactionId),
        CONSTRAINT FK_StockTransactions_Type
            FOREIGN KEY (TransactionTypeId) REFERENCES TransactionTypes (TransactionTypeId),
        CONSTRAINT FK_StockTransactions_Product
            FOREIGN KEY (ProductId)  REFERENCES Products (ProductId),
        CONSTRAINT FK_StockTransactions_Batch
            FOREIGN KEY (BatchId)    REFERENCES ProductBatches (BatchId),
        CONSTRAINT FK_StockTransactions_Location
            FOREIGN KEY (LocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT FK_StockTransactions_Reverses
            FOREIGN KEY (ReversesTransactionId) REFERENCES StockTransactions (StockTransactionId),
        CONSTRAINT CK_StockTransactions_Quantity  CHECK (Quantity > 0),
        CONSTRAINT CK_StockTransactions_Direction CHECK (Direction IN (-1, 1)),
        CONSTRAINT CK_StockTransactions_Rate      CHECK (Rate >= 0)
    );

    -- Product stock ledger for a date range - the single most-run stock query.
    CREATE NONCLUSTERED INDEX IX_StockTransactions_Product_Date
        ON StockTransactions (ProductId, TransactionDate, StockTransactionId)
        INCLUDE (BatchId, LocationId, TransactionTypeId, Quantity, SignedQuantity, Rate, ReferenceNumber);

    -- Batch history, and the rebuild procedure's aggregation source.
    CREATE NONCLUSTERED INDEX IX_StockTransactions_Batch_Date
        ON StockTransactions (BatchId, TransactionDate)
        INCLUDE (SignedQuantity, TransactionTypeId, Rate);

    -- "Show me the stock rows this invoice created" + cancellation reversal.
    CREATE NONCLUSTERED INDEX IX_StockTransactions_Reference
        ON StockTransactions (ReferenceType, ReferenceId)
        INCLUDE (ProductId, BatchId, SignedQuantity, StockTransactionId);

    -- Day-book / daily movement report across all products.
    CREATE NONCLUSTERED INDEX IX_StockTransactions_Date
        ON StockTransactions (TransactionDate)
        INCLUDE (ProductId, TransactionTypeId, SignedQuantity, Value);
END
GO

/*----------------------------------------------------------------------------*/
/* StockAdjustments                                                        */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'StockAdjustments', N'U') IS NULL
BEGIN
    CREATE TABLE StockAdjustments
    (
        AdjustmentId        BIGINT          IDENTITY(1,1) NOT NULL,
        AdjustmentNumber    NVARCHAR(30)    NOT NULL,
        AdjustmentDate      DATE            NOT NULL,
        -- Physical count difference, expiry write-off, damage, or the initial
        -- opening-stock load when the shop migrates onto this system.
        AdjustmentType      NVARCHAR(20)    NOT NULL,
        LocationId          INT             NOT NULL,
        Reason              NVARCHAR(300)   NULL,
        Remarks             NVARCHAR(500)   NULL,

        TotalIncreaseQty    DECIMAL(18,3)   NOT NULL CONSTRAINT DF_StockAdjustments_IncQty DEFAULT (0),
        TotalDecreaseQty    DECIMAL(18,3)   NOT NULL CONSTRAINT DF_StockAdjustments_DecQty DEFAULT (0),
        TotalValueImpact    DECIMAL(18,2)   NOT NULL CONSTRAINT DF_StockAdjustments_Value  DEFAULT (0),

        -- Draft rows touch no stock. Posting is what writes the journal.
        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_StockAdjustments_Status DEFAULT ('Draft'),
        PostedAt            DATETIME2(3)    NULL,
        PostedBy            INT             NULL,
        ApprovedBy          INT             NULL,
        FinancialYearId     INT             NULL,

        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_StockAdjustments_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_StockAdjustments PRIMARY KEY CLUSTERED (AdjustmentId),
        CONSTRAINT UQ_StockAdjustments_Number UNIQUE (AdjustmentNumber),
        CONSTRAINT FK_StockAdjustments_Location
            FOREIGN KEY (LocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT CK_StockAdjustments_Type
            CHECK (AdjustmentType IN ('Physical','Damage','Expiry','Opening','Other')),
        CONSTRAINT CK_StockAdjustments_Status
            CHECK (Status IN ('Draft','Posted','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_StockAdjustments_Date
        ON StockAdjustments (AdjustmentDate) INCLUDE (AdjustmentNumber, AdjustmentType, Status);
END
GO

IF OBJECT_ID(N'StockAdjustmentDetails', N'U') IS NULL
BEGIN
    CREATE TABLE StockAdjustmentDetails
    (
        AdjustmentDetailId  BIGINT          IDENTITY(1,1) NOT NULL,
        AdjustmentId        BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ProductId           INT             NOT NULL,
        BatchId             BIGINT          NOT NULL,

        SystemQty           DECIMAL(18,3)   NOT NULL CONSTRAINT DF_StockAdjustmentDetails_SystemQty DEFAULT (0),
        PhysicalQty         DECIMAL(18,3)   NOT NULL CONSTRAINT DF_StockAdjustmentDetails_PhysicalQty DEFAULT (0),
        -- Signed: negative means shrinkage. Persisted so the header totals and
        -- the variance report never re-derive it inconsistently.
        DifferenceQty       AS (PhysicalQty - SystemQty) PERSISTED,
        Rate                DECIMAL(18,4)   NOT NULL CONSTRAINT DF_StockAdjustmentDetails_Rate DEFAULT (0),
        ValueImpact         AS (CAST((PhysicalQty - SystemQty) * Rate AS DECIMAL(18,2))) PERSISTED,
        Reason              NVARCHAR(300)   NULL,

        CONSTRAINT PK_StockAdjustmentDetails PRIMARY KEY CLUSTERED (AdjustmentDetailId),
        CONSTRAINT FK_StockAdjustmentDetails_Adjustment
            FOREIGN KEY (AdjustmentId) REFERENCES StockAdjustments (AdjustmentId) ON DELETE CASCADE,
        CONSTRAINT FK_StockAdjustmentDetails_Product
            FOREIGN KEY (ProductId) REFERENCES Products (ProductId),
        CONSTRAINT FK_StockAdjustmentDetails_Batch
            FOREIGN KEY (BatchId)   REFERENCES ProductBatches (BatchId),
        CONSTRAINT CK_StockAdjustmentDetails_Qty CHECK (SystemQty >= 0 AND PhysicalQty >= 0)
    );

    CREATE NONCLUSTERED INDEX IX_StockAdjustmentDetails_AdjustmentId
        ON StockAdjustmentDetails (AdjustmentId, LineNumber);

    CREATE NONCLUSTERED INDEX IX_StockAdjustmentDetails_ProductId
        ON StockAdjustmentDetails (ProductId) INCLUDE (BatchId, DifferenceQty);
END
GO

/*----------------------------------------------------------------------------*/
/* StockTransfers                                                          */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'StockTransfers', N'U') IS NULL
BEGIN
    CREATE TABLE StockTransfers
    (
        TransferId          BIGINT          IDENTITY(1,1) NOT NULL,
        TransferNumber      NVARCHAR(30)    NOT NULL,
        TransferDate        DATE            NOT NULL,
        FromLocationId      INT             NOT NULL,
        ToLocationId        INT             NOT NULL,
        TotalQty            DECIMAL(18,3)   NOT NULL CONSTRAINT DF_StockTransfers_TotalQty DEFAULT (0),
        TotalValue          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_StockTransfers_TotalValue DEFAULT (0),
        Remarks             NVARCHAR(500)   NULL,
        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_StockTransfers_Status DEFAULT ('Draft'),
        PostedAt            DATETIME2(3)    NULL,
        PostedBy            INT             NULL,
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_StockTransfers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_StockTransfers PRIMARY KEY CLUSTERED (TransferId),
        CONSTRAINT UQ_StockTransfers_Number UNIQUE (TransferNumber),
        CONSTRAINT FK_StockTransfers_FromLocation
            FOREIGN KEY (FromLocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT FK_StockTransfers_ToLocation
            FOREIGN KEY (ToLocationId)   REFERENCES StorageLocations (LocationId),
        CONSTRAINT CK_StockTransfers_Locations CHECK (FromLocationId <> ToLocationId),
        CONSTRAINT CK_StockTransfers_Status    CHECK (Status IN ('Draft','Posted','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_StockTransfers_Date
        ON StockTransfers (TransferDate) INCLUDE (TransferNumber, FromLocationId, ToLocationId, Status);
END
GO

IF OBJECT_ID(N'StockTransferDetails', N'U') IS NULL
BEGIN
    CREATE TABLE StockTransferDetails
    (
        TransferDetailId    BIGINT          IDENTITY(1,1) NOT NULL,
        TransferId          BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ProductId           INT             NOT NULL,
        -- Source batch at the FROM location.
        FromBatchId         BIGINT          NOT NULL,
        -- Matching batch row at the TO location, created on posting if absent.
        ToBatchId           BIGINT          NULL,
        Quantity            DECIMAL(18,3)   NOT NULL,
        Rate                DECIMAL(18,4)   NOT NULL CONSTRAINT DF_StockTransferDetails_Rate DEFAULT (0),
        LineValue           AS (CAST(Quantity * Rate AS DECIMAL(18,2))) PERSISTED,
        Remarks             NVARCHAR(300)   NULL,

        CONSTRAINT PK_StockTransferDetails PRIMARY KEY CLUSTERED (TransferDetailId),
        CONSTRAINT FK_StockTransferDetails_Transfer
            FOREIGN KEY (TransferId)  REFERENCES StockTransfers (TransferId) ON DELETE CASCADE,
        CONSTRAINT FK_StockTransferDetails_Product
            FOREIGN KEY (ProductId)   REFERENCES Products (ProductId),
        CONSTRAINT FK_StockTransferDetails_FromBatch
            FOREIGN KEY (FromBatchId) REFERENCES ProductBatches (BatchId),
        CONSTRAINT FK_StockTransferDetails_ToBatch
            FOREIGN KEY (ToBatchId)   REFERENCES ProductBatches (BatchId),
        CONSTRAINT CK_StockTransferDetails_Quantity CHECK (Quantity > 0)
    );

    CREATE NONCLUSTERED INDEX IX_StockTransferDetails_TransferId
        ON StockTransferDetails (TransferId, LineNumber);

    CREATE NONCLUSTERED INDEX IX_StockTransferDetails_ProductId
        ON StockTransferDetails (ProductId) INCLUDE (FromBatchId, ToBatchId, Quantity);
END
GO

PRINT N'05_Inventory.sql completed.';
GO
