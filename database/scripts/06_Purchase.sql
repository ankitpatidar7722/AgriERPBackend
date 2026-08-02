/*==============================================================================
  AgriERP  |  06_Purchase.sql
  ------------------------------------------------------------------------------
  Purchase orders, purchase entries and purchase returns.

  THE MONEY MODEL (identical in purchase, sales and both returns)
  --------------------------------------------------------------
  Per line:
        GrossAmount   = Quantity * Rate                      (persisted)
        TaxableAmount = GrossAmount - DiscountAmount         (persisted)
        tax amounts   = TaxableAmount * rate / 100           (written by the app)
        LineTotal     = TaxableAmount + CGST + SGST + IGST + Cess   (persisted)

  Per header:
        GrandTotal    = TaxableAmount + all taxes + Freight + OtherCharges
                        + RoundOff                           (persisted)
        BalanceAmount = GrandTotal - PaidAmount              (persisted)

  Everything that is arithmetic is a PERSISTED computed column. This is the
  point where ERPs usually rot: the UI computes a total, the API recomputes it
  slightly differently, a later patch changes one and not the other, and the
  printed bill stops matching the stored total. Here the database owns the
  arithmetic, so a total that disagrees with its own lines is not expressible.

  CGST+SGST vs IGST is decided by IsInterState, set from the shop's state code
  against the supplier's. Both column sets exist on every document because GST
  returns are filed on what was charged, not on what would be charged today.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* PurchaseOrders                                                          */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'PurchaseOrders', N'U') IS NULL
BEGIN
    CREATE TABLE PurchaseOrders
    (
        PurchaseOrderId     BIGINT          IDENTITY(1,1) NOT NULL,
        OrderNumber         NVARCHAR(30)    NOT NULL,
        OrderDate           DATE            NOT NULL,
        ExpectedDate        DATE            NULL,
        SupplierId          INT             NOT NULL,
        LocationId          INT             NOT NULL,
        Remarks             NVARCHAR(500)   NULL,

        TotalQty            DECIMAL(18,3)   NOT NULL CONSTRAINT DF_PurchaseOrders_TotalQty DEFAULT (0),
        EstimatedValue      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseOrders_EstValue DEFAULT (0),

        -- Open -> Partial -> Received, or Cancelled. Drives the "pending
        -- purchase" screen you asked for.
        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_PurchaseOrders_Status DEFAULT ('Draft'),
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_PurchaseOrders_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_PurchaseOrders PRIMARY KEY CLUSTERED (PurchaseOrderId),
        CONSTRAINT UQ_PurchaseOrders_Number UNIQUE (OrderNumber),
        CONSTRAINT FK_PurchaseOrders_Supplier
            FOREIGN KEY (SupplierId) REFERENCES Suppliers (SupplierId),
        CONSTRAINT FK_PurchaseOrders_Location
            FOREIGN KEY (LocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT CK_PurchaseOrders_Status
            CHECK (Status IN ('Draft','Open','Partial','Received','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_PurchaseOrders_Supplier_Date
        ON PurchaseOrders (SupplierId, OrderDate DESC) INCLUDE (OrderNumber, Status, EstimatedValue);

    CREATE NONCLUSTERED INDEX IX_PurchaseOrders_Status
        ON PurchaseOrders (Status, ExpectedDate) INCLUDE (OrderNumber, SupplierId);
END
GO

IF OBJECT_ID(N'PurchaseOrderDetails', N'U') IS NULL
BEGIN
    CREATE TABLE PurchaseOrderDetails
    (
        PurchaseOrderDetailId BIGINT        IDENTITY(1,1) NOT NULL,
        PurchaseOrderId     BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ProductId           INT             NOT NULL,
        OrderedQty          DECIMAL(18,3)   NOT NULL,
        -- Maintained as goods arrive; OrderedQty - ReceivedQty is what is still
        -- pending, which is how the PO status is derived.
        ReceivedQty         DECIMAL(18,3)   NOT NULL CONSTRAINT DF_PurchaseOrderDetails_ReceivedQty DEFAULT (0),
        PendingQty          AS (OrderedQty - ReceivedQty) PERSISTED,
        UnitId              INT             NOT NULL,
        Rate                DECIMAL(18,4)   NOT NULL CONSTRAINT DF_PurchaseOrderDetails_Rate DEFAULT (0),
        EstimatedAmount     AS (CAST(OrderedQty * Rate AS DECIMAL(18,2))) PERSISTED,
        Remarks             NVARCHAR(300)   NULL,

        CONSTRAINT PK_PurchaseOrderDetails PRIMARY KEY CLUSTERED (PurchaseOrderDetailId),
        CONSTRAINT FK_PurchaseOrderDetails_Order
            FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders (PurchaseOrderId) ON DELETE CASCADE,
        CONSTRAINT FK_PurchaseOrderDetails_Product
            FOREIGN KEY (ProductId) REFERENCES Products (ProductId),
        CONSTRAINT FK_PurchaseOrderDetails_Unit
            FOREIGN KEY (UnitId)    REFERENCES Units (UnitId),
        CONSTRAINT CK_PurchaseOrderDetails_Qty
            CHECK (OrderedQty > 0 AND ReceivedQty >= 0 AND ReceivedQty <= OrderedQty)
    );

    CREATE NONCLUSTERED INDEX IX_PurchaseOrderDetails_OrderId
        ON PurchaseOrderDetails (PurchaseOrderId, LineNumber);

    CREATE NONCLUSTERED INDEX IX_PurchaseOrderDetails_ProductId
        ON PurchaseOrderDetails (ProductId) INCLUDE (PurchaseOrderId, PendingQty);
END
GO

/*----------------------------------------------------------------------------*/
/* Purchases  (PurchaseMaster)                                             */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Purchases', N'U') IS NULL
BEGIN
    CREATE TABLE Purchases
    (
        PurchaseId              BIGINT          IDENTITY(1,1) NOT NULL,
        PurchaseNumber          NVARCHAR(30)    NOT NULL,      -- our internal number
        PurchaseDate            DATE            NOT NULL,
        SupplierId              INT             NOT NULL,
        -- The supplier's own bill number. Needed for GSTR-2 reconciliation,
        -- and the pair (supplier, their invoice no) must not repeat.
        SupplierInvoiceNumber   NVARCHAR(50)    NULL,
        SupplierInvoiceDate     DATE            NULL,
        PurchaseOrderId         BIGINT          NULL,
        LocationId              INT             NOT NULL,

        IsInterState            BIT             NOT NULL CONSTRAINT DF_Purchases_IsInterState DEFAULT (0),
        SupplierStateId         INT             NULL,

        /* ---- amounts ---- */
        GrossAmount             DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_GrossAmount   DEFAULT (0),
        DiscountAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_DiscountAmount DEFAULT (0),
        TaxableAmount           DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_TaxableAmount DEFAULT (0),
        CgstAmount              DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_CgstAmount    DEFAULT (0),
        SgstAmount              DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_SgstAmount    DEFAULT (0),
        IgstAmount              DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_IgstAmount    DEFAULT (0),
        CessAmount              DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_CessAmount    DEFAULT (0),
        FreightCharges          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_Freight       DEFAULT (0),
        OtherCharges            DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_OtherCharges  DEFAULT (0),
        RoundOff                DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_RoundOff      DEFAULT (0),
        GrandTotal              AS (TaxableAmount + CgstAmount + SgstAmount + IgstAmount
                                    + CessAmount + FreightCharges + OtherCharges + RoundOff) PERSISTED,

        PaidAmount              DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Purchases_PaidAmount    DEFAULT (0),
        BalanceAmount           AS (TaxableAmount + CgstAmount + SgstAmount + IgstAmount
                                    + CessAmount + FreightCharges + OtherCharges + RoundOff
                                    - PaidAmount) PERSISTED,
        PaymentStatus           AS (CASE
                                      WHEN PaidAmount <= 0 THEN N'Unpaid'
                                      WHEN PaidAmount >= (TaxableAmount + CgstAmount + SgstAmount
                                           + IgstAmount + CessAmount + FreightCharges
                                           + OtherCharges + RoundOff) THEN N'Paid'
                                      ELSE N'Partial'
                                    END) PERSISTED,
        DueDate                 DATE            NULL,

        Status                  NVARCHAR(15)    NOT NULL CONSTRAINT DF_Purchases_Status DEFAULT ('Draft'),
        PostedAt                DATETIME2(3)    NULL,
        PostedBy                INT             NULL,
        CancelledAt             DATETIME2(3)    NULL,
        CancelledBy             INT             NULL,
        CancelReason            NVARCHAR(300)   NULL,

        Remarks                 NVARCHAR(500)   NULL,
        FinancialYearId         INT             NULL,
        CreatedAt               DATETIME2(3)    NOT NULL CONSTRAINT DF_Purchases_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy               INT             NULL,
        UpdatedAt               DATETIME2(3)    NULL,
        UpdatedBy               INT             NULL,
        RowVersion              ROWVERSION      NOT NULL,

        CONSTRAINT PK_Purchases PRIMARY KEY CLUSTERED (PurchaseId),
        CONSTRAINT UQ_Purchases_PurchaseNumber UNIQUE (PurchaseNumber),
        CONSTRAINT FK_Purchases_Supplier
            FOREIGN KEY (SupplierId)      REFERENCES Suppliers (SupplierId),
        CONSTRAINT FK_Purchases_Order
            FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders (PurchaseOrderId),
        CONSTRAINT FK_Purchases_Location
            FOREIGN KEY (LocationId)      REFERENCES StorageLocations (LocationId),
        CONSTRAINT FK_Purchases_State
            FOREIGN KEY (SupplierStateId) REFERENCES States (StateId),
        CONSTRAINT CK_Purchases_Status  CHECK (Status IN ('Draft','Posted','Cancelled')),
        CONSTRAINT CK_Purchases_Amounts CHECK (
            GrossAmount >= 0 AND DiscountAmount >= 0 AND TaxableAmount >= 0 AND
            CgstAmount >= 0 AND SgstAmount >= 0 AND IgstAmount >= 0 AND
            CessAmount >= 0 AND PaidAmount >= 0),
        -- An inter-state bill carries IGST only; an intra-state bill never does.
        CONSTRAINT CK_Purchases_TaxMode CHECK (
            (IsInterState = 1 AND CgstAmount = 0 AND SgstAmount = 0) OR
            (IsInterState = 0 AND IgstAmount = 0))
    );

    -- Blocks the same supplier bill being entered twice - the most common and
    -- most expensive data-entry error in a purchase module.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Purchases_Supplier_InvoiceNumber
        ON Purchases (SupplierId, SupplierInvoiceNumber)
        WHERE SupplierInvoiceNumber IS NOT NULL AND Status <> 'Cancelled';

    CREATE NONCLUSTERED INDEX IX_Purchases_Supplier_Date
        ON Purchases (SupplierId, PurchaseDate DESC)
        INCLUDE (PurchaseNumber, GrandTotal, PaidAmount, BalanceAmount, Status);

    CREATE NONCLUSTERED INDEX IX_Purchases_PurchaseDate
        ON Purchases (PurchaseDate)
        INCLUDE (SupplierId, PurchaseNumber, TaxableAmount, CgstAmount, SgstAmount, IgstAmount, GrandTotal, Status);

    -- Drives the supplier-outstanding view and the payables ageing report.
    CREATE NONCLUSTERED INDEX IX_Purchases_Outstanding
        ON Purchases (SupplierId, DueDate)
        INCLUDE (PurchaseNumber, PurchaseDate, GrandTotal, PaidAmount, BalanceAmount)
        WHERE Status = 'Posted';
END
GO

IF OBJECT_ID(N'PurchaseDetails', N'U') IS NULL
BEGIN
    CREATE TABLE PurchaseDetails
    (
        PurchaseDetailId    BIGINT          IDENTITY(1,1) NOT NULL,
        PurchaseId          BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ProductId           INT             NOT NULL,
        -- Resolved / created when the purchase is posted.
        BatchId             BIGINT          NULL,

        /* Batch data as keyed in. Kept on the line as well as on the batch row
           so the printed bill still reproduces exactly what was entered even
           if the batch master is later corrected. */
        BatchNumber         NVARCHAR(50)    NULL,
        ManufacturingDate   DATE            NULL,
        ExpiryDate          DATE            NULL,

        Quantity            DECIMAL(18,3)   NOT NULL,
        -- Scheme goods: "10 + 1 free". Adds to stock, adds nothing to cost,
        -- which is precisely why it lowers the average purchase rate.
        FreeQuantity        DECIMAL(18,3)   NOT NULL CONSTRAINT DF_PurchaseDetails_FreeQty DEFAULT (0),
        TotalQuantity       AS (Quantity + FreeQuantity) PERSISTED,
        UnitId              INT             NOT NULL,

        Rate                DECIMAL(18,4)   NOT NULL,
        GrossAmount         AS (CAST(Quantity * Rate AS DECIMAL(18,2))) PERSISTED,
        DiscountPercent     DECIMAL(6,3)    NOT NULL CONSTRAINT DF_PurchaseDetails_DiscPct DEFAULT (0),
        DiscountAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseDetails_DiscAmt DEFAULT (0),
        TaxableAmount       AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount) PERSISTED,

        GstPercent          DECIMAL(6,3)    NOT NULL CONSTRAINT DF_PurchaseDetails_GstPct  DEFAULT (0),
        CgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseDetails_Cgst    DEFAULT (0),
        SgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseDetails_Sgst    DEFAULT (0),
        IgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseDetails_Igst    DEFAULT (0),
        CessAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseDetails_Cess    DEFAULT (0),
        LineTotal           AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount
                                + CgstAmount + SgstAmount + IgstAmount + CessAmount) PERSISTED,

        -- Landed cost per unit including freight share and free goods. This is
        -- what a sale copies as its cost, so profit is real rather than notional.
        LandedRate          DECIMAL(18,4)   NOT NULL CONSTRAINT DF_PurchaseDetails_LandedRate DEFAULT (0),

        -- New MRP / selling rate arriving with this consignment; posting pushes
        -- these onto the batch and, optionally, onto the product master.
        Mrp                 DECIMAL(18,4)   NOT NULL CONSTRAINT DF_PurchaseDetails_Mrp DEFAULT (0),
        SellingRate         DECIMAL(18,4)   NOT NULL CONSTRAINT DF_PurchaseDetails_SellingRate DEFAULT (0),

        HsnCode             NVARCHAR(10)    NULL,
        Remarks             NVARCHAR(300)   NULL,

        CONSTRAINT PK_PurchaseDetails PRIMARY KEY CLUSTERED (PurchaseDetailId),
        CONSTRAINT FK_PurchaseDetails_Purchase
            FOREIGN KEY (PurchaseId) REFERENCES Purchases (PurchaseId) ON DELETE CASCADE,
        CONSTRAINT FK_PurchaseDetails_Product
            FOREIGN KEY (ProductId)  REFERENCES Products (ProductId),
        CONSTRAINT FK_PurchaseDetails_Batch
            FOREIGN KEY (BatchId)    REFERENCES ProductBatches (BatchId),
        CONSTRAINT FK_PurchaseDetails_Unit
            FOREIGN KEY (UnitId)     REFERENCES Units (UnitId),
        CONSTRAINT CK_PurchaseDetails_Quantity CHECK (Quantity > 0 AND FreeQuantity >= 0),
        CONSTRAINT CK_PurchaseDetails_Rate     CHECK (Rate >= 0 AND LandedRate >= 0),
        CONSTRAINT CK_PurchaseDetails_Discount CHECK (DiscountAmount >= 0 AND DiscountPercent >= 0 AND DiscountPercent <= 100)
    );

    CREATE NONCLUSTERED INDEX IX_PurchaseDetails_PurchaseId
        ON PurchaseDetails (PurchaseId, LineNumber);

    -- Purchase history for one product: last rate paid, rate trend, supplier mix.
    CREATE NONCLUSTERED INDEX IX_PurchaseDetails_ProductId
        ON PurchaseDetails (ProductId)
        INCLUDE (PurchaseId, BatchId, Quantity, FreeQuantity, Rate, LandedRate, ExpiryDate);

    CREATE NONCLUSTERED INDEX IX_PurchaseDetails_BatchId
        ON PurchaseDetails (BatchId) INCLUDE (PurchaseId, ProductId, Quantity, LandedRate);
END
GO

/*----------------------------------------------------------------------------*/
/* PurchaseReturns                                                         */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'PurchaseReturns', N'U') IS NULL
BEGIN
    CREATE TABLE PurchaseReturns
    (
        PurchaseReturnId    BIGINT          IDENTITY(1,1) NOT NULL,
        ReturnNumber        NVARCHAR(30)    NOT NULL,
        ReturnDate          DATE            NOT NULL,
        SupplierId          INT             NOT NULL,
        -- Optional: expired stock is often returned without citing a bill.
        PurchaseId          BIGINT          NULL,
        LocationId          INT             NOT NULL,
        DebitNoteNumber     NVARCHAR(50)    NULL,
        ReturnReason        NVARCHAR(300)   NULL,

        IsInterState        BIT             NOT NULL CONSTRAINT DF_PurchaseReturns_IsInterState DEFAULT (0),
        GrossAmount         DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_Gross    DEFAULT (0),
        DiscountAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_Discount DEFAULT (0),
        TaxableAmount       DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_Taxable  DEFAULT (0),
        CgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_Cgst     DEFAULT (0),
        SgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_Sgst     DEFAULT (0),
        IgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_Igst     DEFAULT (0),
        CessAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_Cess     DEFAULT (0),
        RoundOff            DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturns_RoundOff DEFAULT (0),
        GrandTotal          AS (TaxableAmount + CgstAmount + SgstAmount + IgstAmount
                                + CessAmount + RoundOff) PERSISTED,

        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_PurchaseReturns_Status DEFAULT ('Draft'),
        PostedAt            DATETIME2(3)    NULL,
        PostedBy            INT             NULL,
        Remarks             NVARCHAR(500)   NULL,
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_PurchaseReturns_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_PurchaseReturns PRIMARY KEY CLUSTERED (PurchaseReturnId),
        CONSTRAINT UQ_PurchaseReturns_Number UNIQUE (ReturnNumber),
        CONSTRAINT FK_PurchaseReturns_Supplier
            FOREIGN KEY (SupplierId) REFERENCES Suppliers (SupplierId),
        CONSTRAINT FK_PurchaseReturns_Purchase
            FOREIGN KEY (PurchaseId) REFERENCES Purchases (PurchaseId),
        CONSTRAINT FK_PurchaseReturns_Location
            FOREIGN KEY (LocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT CK_PurchaseReturns_Status CHECK (Status IN ('Draft','Posted','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_PurchaseReturns_Supplier_Date
        ON PurchaseReturns (SupplierId, ReturnDate DESC) INCLUDE (ReturnNumber, GrandTotal, Status);

    CREATE NONCLUSTERED INDEX IX_PurchaseReturns_Date
        ON PurchaseReturns (ReturnDate) INCLUDE (SupplierId, TaxableAmount, GrandTotal, Status);
END
GO

IF OBJECT_ID(N'PurchaseReturnDetails', N'U') IS NULL
BEGIN
    CREATE TABLE PurchaseReturnDetails
    (
        PurchaseReturnDetailId BIGINT       IDENTITY(1,1) NOT NULL,
        PurchaseReturnId    BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ProductId           INT             NOT NULL,
        BatchId             BIGINT          NOT NULL,
        PurchaseDetailId    BIGINT          NULL,          -- original line, when known

        Quantity            DECIMAL(18,3)   NOT NULL,
        UnitId              INT             NOT NULL,
        Rate                DECIMAL(18,4)   NOT NULL,
        GrossAmount         AS (CAST(Quantity * Rate AS DECIMAL(18,2))) PERSISTED,
        DiscountAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturnDetails_Disc DEFAULT (0),
        TaxableAmount       AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount) PERSISTED,
        GstPercent          DECIMAL(6,3)    NOT NULL CONSTRAINT DF_PurchaseReturnDetails_GstPct DEFAULT (0),
        CgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturnDetails_Cgst DEFAULT (0),
        SgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturnDetails_Sgst DEFAULT (0),
        IgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturnDetails_Igst DEFAULT (0),
        CessAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_PurchaseReturnDetails_Cess DEFAULT (0),
        LineTotal           AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount
                                + CgstAmount + SgstAmount + IgstAmount + CessAmount) PERSISTED,
        ReturnReason        NVARCHAR(300)   NULL,

        CONSTRAINT PK_PurchaseReturnDetails PRIMARY KEY CLUSTERED (PurchaseReturnDetailId),
        CONSTRAINT FK_PurchaseReturnDetails_Return
            FOREIGN KEY (PurchaseReturnId) REFERENCES PurchaseReturns (PurchaseReturnId) ON DELETE CASCADE,
        CONSTRAINT FK_PurchaseReturnDetails_Product
            FOREIGN KEY (ProductId)        REFERENCES Products (ProductId),
        CONSTRAINT FK_PurchaseReturnDetails_Batch
            FOREIGN KEY (BatchId)          REFERENCES ProductBatches (BatchId),
        CONSTRAINT FK_PurchaseReturnDetails_PurchaseDetail
            FOREIGN KEY (PurchaseDetailId) REFERENCES PurchaseDetails (PurchaseDetailId),
        CONSTRAINT FK_PurchaseReturnDetails_Unit
            FOREIGN KEY (UnitId)           REFERENCES Units (UnitId),
        CONSTRAINT CK_PurchaseReturnDetails_Quantity CHECK (Quantity > 0)
    );

    CREATE NONCLUSTERED INDEX IX_PurchaseReturnDetails_ReturnId
        ON PurchaseReturnDetails (PurchaseReturnId, LineNumber);

    CREATE NONCLUSTERED INDEX IX_PurchaseReturnDetails_ProductId
        ON PurchaseReturnDetails (ProductId) INCLUDE (BatchId, Quantity, Rate);
END
GO

PRINT N'06_Purchase.sql completed.';
GO
