/*==============================================================================
  AgriERP  |  07_Sales.sql
  ------------------------------------------------------------------------------
  Sales invoices, per-invoice payment splits and sales returns.

  Two things worth reading before the DDL
  ---------------------------------------
  1. SALE TYPE AND PAYMENT TYPE ARE SEPARATE AXES.
     You listed Retail / Wholesale / Cash / Credit as one list, but they are two
     independent questions: which price list applies (Retail, Wholesale, Dealer)
     and how it was settled (Cash, Credit). A wholesale sale on credit is an
     everyday transaction, and one column cannot express it. Hence SaleType and
     PaymentType. CK_Sales_CreditNeedsCustomer then enforces the rule that
     actually matters: you cannot give credit to a walk-in nobody.

  2. COST IS COPIED ONTO THE SALE LINE, NOT LOOKED UP LATER.
     CostRate on SalesDetails is the landed rate of the batch that left the
     shelf, frozen at the moment of sale. Profit reports read it directly.
     Deriving profit later from the product's current purchase rate would
     silently restate last year's profit every time a new consignment arrives
     at a different price - a classic and very hard-to-notice reporting bug.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* Sales  (SalesMaster)                                                    */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Sales', N'U') IS NULL
BEGIN
    CREATE TABLE Sales
    (
        SaleId              BIGINT          IDENTITY(1,1) NOT NULL,
        InvoiceNumber       NVARCHAR(30)    NOT NULL,
        InvoiceDate         DATE            NOT NULL,
        InvoiceTime         TIME(0)         NULL,          -- counter rush analysis

        -- NULL for a pure walk-in cash sale. The two WalkIn columns capture the
        -- name and mobile taken at the counter without polluting the customer
        -- master with hundreds of one-time entries.
        CustomerId          INT             NULL,
        WalkInCustomerName  NVARCHAR(150)   NULL,
        WalkInMobile        NVARCHAR(15)    NULL,

        SaleType            NVARCHAR(15)    NOT NULL CONSTRAINT DF_Sales_SaleType    DEFAULT ('Retail'),
        PaymentType         NVARCHAR(10)    NOT NULL CONSTRAINT DF_Sales_PaymentType DEFAULT ('Cash'),
        LocationId          INT             NOT NULL,
        SalesmanId          INT             NULL,          -- Users, for incentive reports

        IsInterState        BIT             NOT NULL CONSTRAINT DF_Sales_IsInterState DEFAULT (0),
        PlaceOfSupplyStateId INT            NULL,

        /* ---- amounts ---- */
        GrossAmount         DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_GrossAmount    DEFAULT (0),
        DiscountAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_DiscountAmount DEFAULT (0),
        TaxableAmount       DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_TaxableAmount  DEFAULT (0),
        CgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_CgstAmount     DEFAULT (0),
        SgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_SgstAmount     DEFAULT (0),
        IgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_IgstAmount     DEFAULT (0),
        CessAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_CessAmount     DEFAULT (0),
        OtherCharges        DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_OtherCharges   DEFAULT (0),
        RoundOff            DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_RoundOff       DEFAULT (0),
        GrandTotal          AS (TaxableAmount + CgstAmount + SgstAmount + IgstAmount
                                + CessAmount + OtherCharges + RoundOff) PERSISTED,

        -- Total cost of goods on this invoice, summed from the lines. Makes
        -- invoice-level and day-level profit a single-column read.
        TotalCostAmount     DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_TotalCostAmount DEFAULT (0),
        GrossProfit         AS (TaxableAmount - TotalCostAmount) PERSISTED,

        ReceivedAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Sales_ReceivedAmount DEFAULT (0),
        BalanceAmount       AS (TaxableAmount + CgstAmount + SgstAmount + IgstAmount
                                + CessAmount + OtherCharges + RoundOff
                                - ReceivedAmount) PERSISTED,
        PaymentStatus       AS (CASE
                                  WHEN ReceivedAmount <= 0 THEN N'Unpaid'
                                  WHEN ReceivedAmount >= (TaxableAmount + CgstAmount + SgstAmount
                                       + IgstAmount + CessAmount + OtherCharges
                                       + RoundOff) THEN N'Paid'
                                  ELSE N'Partial'
                                END) PERSISTED,
        DueDate             DATE            NULL,

        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_Sales_Status DEFAULT ('Draft'),
        PostedAt            DATETIME2(3)    NULL,
        PostedBy            INT             NULL,
        CancelledAt         DATETIME2(3)    NULL,
        CancelledBy         INT             NULL,
        CancelReason        NVARCHAR(300)   NULL,
        PrintCount          INT             NOT NULL CONSTRAINT DF_Sales_PrintCount DEFAULT (0),

        Remarks             NVARCHAR(500)   NULL,
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Sales_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_Sales PRIMARY KEY CLUSTERED (SaleId),
        CONSTRAINT UQ_Sales_InvoiceNumber UNIQUE (InvoiceNumber),
        CONSTRAINT FK_Sales_Customer
            FOREIGN KEY (CustomerId)           REFERENCES Customers (CustomerId),
        CONSTRAINT FK_Sales_Location
            FOREIGN KEY (LocationId)           REFERENCES StorageLocations (LocationId),
        CONSTRAINT FK_Sales_Salesman
            FOREIGN KEY (SalesmanId)           REFERENCES Users (UserId),
        CONSTRAINT FK_Sales_PlaceOfSupply
            FOREIGN KEY (PlaceOfSupplyStateId) REFERENCES States (StateId),

        CONSTRAINT CK_Sales_SaleType    CHECK (SaleType    IN ('Retail','Wholesale','Dealer')),
        CONSTRAINT CK_Sales_PaymentType CHECK (PaymentType IN ('Cash','Credit')),
        CONSTRAINT CK_Sales_Status      CHECK (Status      IN ('Draft','Posted','Cancelled')),
        -- Credit is extended to a known customer or not at all.
        CONSTRAINT CK_Sales_CreditNeedsCustomer
            CHECK (PaymentType = 'Cash' OR CustomerId IS NOT NULL),
        CONSTRAINT CK_Sales_Amounts CHECK (
            GrossAmount >= 0 AND DiscountAmount >= 0 AND TaxableAmount >= 0 AND
            CgstAmount >= 0 AND SgstAmount >= 0 AND IgstAmount >= 0 AND
            CessAmount >= 0 AND ReceivedAmount >= 0),
        CONSTRAINT CK_Sales_TaxMode CHECK (
            (IsInterState = 1 AND CgstAmount = 0 AND SgstAmount = 0) OR
            (IsInterState = 0 AND IgstAmount = 0))
    );

    CREATE NONCLUSTERED INDEX IX_Sales_InvoiceDate
        ON Sales (InvoiceDate)
        INCLUDE (InvoiceNumber, CustomerId, TaxableAmount, CgstAmount, SgstAmount,
                 IgstAmount, GrandTotal, TotalCostAmount, GrossProfit, Status, PaymentType);

    CREATE NONCLUSTERED INDEX IX_Sales_Customer_Date
        ON Sales (CustomerId, InvoiceDate DESC)
        INCLUDE (InvoiceNumber, GrandTotal, ReceivedAmount, BalanceAmount, Status)
        WHERE CustomerId IS NOT NULL;

    -- Customer-due / receivables ageing.
    CREATE NONCLUSTERED INDEX IX_Sales_Outstanding
        ON Sales (CustomerId, DueDate)
        INCLUDE (InvoiceNumber, InvoiceDate, GrandTotal, ReceivedAmount, BalanceAmount)
        WHERE Status = 'Posted' AND CustomerId IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_Sales_WalkInMobile
        ON Sales (WalkInMobile) INCLUDE (InvoiceNumber, InvoiceDate, GrandTotal)
        WHERE WalkInMobile IS NOT NULL;
END
GO

/*----------------------------------------------------------------------------*/
/* SalesDetails                                                            */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'SalesDetails', N'U') IS NULL
BEGIN
    CREATE TABLE SalesDetails
    (
        SalesDetailId       BIGINT          IDENTITY(1,1) NOT NULL,
        SaleId              BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ProductId           INT             NOT NULL,
        -- Which physical lot left the shelf. Mandatory: without it there is no
        -- expiry traceability and no honest cost.
        BatchId             BIGINT          NOT NULL,
        BatchNumber         NVARCHAR(50)    NULL,          -- printed on the bill
        ExpiryDate          DATE            NULL,          -- printed on the bill

        Quantity            DECIMAL(18,3)   NOT NULL,
        FreeQuantity        DECIMAL(18,3)   NOT NULL CONSTRAINT DF_SalesDetails_FreeQty DEFAULT (0),
        TotalQuantity       AS (Quantity + FreeQuantity) PERSISTED,
        UnitId              INT             NOT NULL,

        Mrp                 DECIMAL(18,4)   NOT NULL CONSTRAINT DF_SalesDetails_Mrp  DEFAULT (0),
        Rate                DECIMAL(18,4)   NOT NULL,
        GrossAmount         AS (CAST(Quantity * Rate AS DECIMAL(18,2))) PERSISTED,
        DiscountPercent     DECIMAL(6,3)    NOT NULL CONSTRAINT DF_SalesDetails_DiscPct DEFAULT (0),
        DiscountAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesDetails_DiscAmt DEFAULT (0),
        TaxableAmount       AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount) PERSISTED,

        GstPercent          DECIMAL(6,3)    NOT NULL CONSTRAINT DF_SalesDetails_GstPct DEFAULT (0),
        CgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesDetails_Cgst   DEFAULT (0),
        SgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesDetails_Sgst   DEFAULT (0),
        IgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesDetails_Igst   DEFAULT (0),
        CessAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesDetails_Cess   DEFAULT (0),
        LineTotal           AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount
                                + CgstAmount + SgstAmount + IgstAmount + CessAmount) PERSISTED,

        -- Frozen cost of the batch sold. Free goods carry cost but earn nothing,
        -- so cost is charged on TotalQuantity while revenue is on Quantity.
        CostRate            DECIMAL(18,4)   NOT NULL CONSTRAINT DF_SalesDetails_CostRate DEFAULT (0),
        CostAmount          AS (CAST((Quantity + FreeQuantity) * CostRate AS DECIMAL(18,2))) PERSISTED,
        LineProfit          AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount
                                - CAST((Quantity + FreeQuantity) * CostRate AS DECIMAL(18,2))) PERSISTED,

        HsnCode             NVARCHAR(10)    NULL,
        Remarks             NVARCHAR(300)   NULL,

        CONSTRAINT PK_SalesDetails PRIMARY KEY CLUSTERED (SalesDetailId),
        CONSTRAINT FK_SalesDetails_Sale
            FOREIGN KEY (SaleId)    REFERENCES Sales (SaleId) ON DELETE CASCADE,
        CONSTRAINT FK_SalesDetails_Product
            FOREIGN KEY (ProductId) REFERENCES Products (ProductId),
        CONSTRAINT FK_SalesDetails_Batch
            FOREIGN KEY (BatchId)   REFERENCES ProductBatches (BatchId),
        CONSTRAINT FK_SalesDetails_Unit
            FOREIGN KEY (UnitId)    REFERENCES Units (UnitId),
        CONSTRAINT CK_SalesDetails_Quantity CHECK (Quantity > 0 AND FreeQuantity >= 0),
        CONSTRAINT CK_SalesDetails_Rate     CHECK (Rate >= 0 AND CostRate >= 0),
        CONSTRAINT CK_SalesDetails_Discount CHECK (DiscountAmount >= 0 AND DiscountPercent >= 0 AND DiscountPercent <= 100)
    );

    CREATE NONCLUSTERED INDEX IX_SalesDetails_SaleId
        ON SalesDetails (SaleId, LineNumber);

    -- Top-selling products, product-wise sales and profit.
    CREATE NONCLUSTERED INDEX IX_SalesDetails_ProductId
        ON SalesDetails (ProductId)
        INCLUDE (SaleId, BatchId, Quantity, TotalQuantity, Rate, TaxableAmount, CostAmount, LineProfit);

    CREATE NONCLUSTERED INDEX IX_SalesDetails_BatchId
        ON SalesDetails (BatchId) INCLUDE (SaleId, ProductId, Quantity, CostRate);
END
GO

/*----------------------------------------------------------------------------*/
/* SalePayments  - the "multiple payment modes" split                      */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'SalePayments', N'U') IS NULL
BEGIN
    CREATE TABLE SalePayments
    (
        SalePaymentId       BIGINT          IDENTITY(1,1) NOT NULL,
        SaleId              BIGINT          NOT NULL,
        PaymentModeId       INT             NOT NULL,
        Amount              DECIMAL(18,2)   NOT NULL,
        ReferenceNumber     NVARCHAR(60)    NULL,          -- UPI ref / cheque no / card auth
        BankName            NVARCHAR(120)   NULL,
        ChequeDate          DATE            NULL,
        Remarks             NVARCHAR(300)   NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_SalePayments_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,

        CONSTRAINT PK_SalePayments PRIMARY KEY CLUSTERED (SalePaymentId),
        CONSTRAINT FK_SalePayments_Sale
            FOREIGN KEY (SaleId) REFERENCES Sales (SaleId) ON DELETE CASCADE,
        CONSTRAINT CK_SalePayments_Amount CHECK (Amount > 0)
        -- FK to PaymentModes is added in 08_Finance.sql, after that table exists.
    );

    CREATE NONCLUSTERED INDEX IX_SalePayments_SaleId
        ON SalePayments (SaleId) INCLUDE (PaymentModeId, Amount);

    -- Daily cash / UPI / card reconciliation at closing time.
    CREATE NONCLUSTERED INDEX IX_SalePayments_ModeId
        ON SalePayments (PaymentModeId, CreatedAt) INCLUDE (Amount, SaleId);
END
GO

/*----------------------------------------------------------------------------*/
/* SalesReturns                                                            */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'SalesReturns', N'U') IS NULL
BEGIN
    CREATE TABLE SalesReturns
    (
        SalesReturnId       BIGINT          IDENTITY(1,1) NOT NULL,
        ReturnNumber        NVARCHAR(30)    NOT NULL,
        ReturnDate          DATE            NOT NULL,
        CustomerId          INT             NULL,
        SaleId              BIGINT          NULL,          -- original invoice, when produced
        LocationId          INT             NOT NULL,
        CreditNoteNumber    NVARCHAR(50)    NULL,
        ReturnReason        NVARCHAR(300)   NULL,

        IsInterState        BIT             NOT NULL CONSTRAINT DF_SalesReturns_IsInterState DEFAULT (0),
        GrossAmount         DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Gross    DEFAULT (0),
        DiscountAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Discount DEFAULT (0),
        TaxableAmount       DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Taxable  DEFAULT (0),
        CgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Cgst     DEFAULT (0),
        SgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Sgst     DEFAULT (0),
        IgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Igst     DEFAULT (0),
        CessAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Cess     DEFAULT (0),
        RoundOff            DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_RoundOff DEFAULT (0),
        GrandTotal          AS (TaxableAmount + CgstAmount + SgstAmount + IgstAmount
                                + CessAmount + RoundOff) PERSISTED,
        TotalCostAmount     DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_TotalCost DEFAULT (0),

        -- Cash back over the counter, or knocked off the customer's ledger.
        RefundMode          NVARCHAR(15)    NOT NULL CONSTRAINT DF_SalesReturns_RefundMode DEFAULT ('Adjust'),
        RefundedAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturns_Refunded   DEFAULT (0),

        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_SalesReturns_Status DEFAULT ('Draft'),
        PostedAt            DATETIME2(3)    NULL,
        PostedBy            INT             NULL,
        Remarks             NVARCHAR(500)   NULL,
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_SalesReturns_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_SalesReturns PRIMARY KEY CLUSTERED (SalesReturnId),
        CONSTRAINT UQ_SalesReturns_Number UNIQUE (ReturnNumber),
        CONSTRAINT FK_SalesReturns_Customer
            FOREIGN KEY (CustomerId) REFERENCES Customers (CustomerId),
        CONSTRAINT FK_SalesReturns_Sale
            FOREIGN KEY (SaleId)     REFERENCES Sales (SaleId),
        CONSTRAINT FK_SalesReturns_Location
            FOREIGN KEY (LocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT CK_SalesReturns_Status     CHECK (Status     IN ('Draft','Posted','Cancelled')),
        CONSTRAINT CK_SalesReturns_RefundMode CHECK (RefundMode IN ('Cash','Adjust','Bank','Replacement'))
    );

    CREATE NONCLUSTERED INDEX IX_SalesReturns_Date
        ON SalesReturns (ReturnDate) INCLUDE (CustomerId, TaxableAmount, GrandTotal, Status);

    CREATE NONCLUSTERED INDEX IX_SalesReturns_Customer
        ON SalesReturns (CustomerId, ReturnDate DESC) INCLUDE (ReturnNumber, GrandTotal, Status)
        WHERE CustomerId IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_SalesReturns_SaleId
        ON SalesReturns (SaleId) WHERE SaleId IS NOT NULL;
END
GO

IF OBJECT_ID(N'SalesReturnDetails', N'U') IS NULL
BEGIN
    CREATE TABLE SalesReturnDetails
    (
        SalesReturnDetailId BIGINT          IDENTITY(1,1) NOT NULL,
        SalesReturnId       BIGINT          NOT NULL,
        LineNumber          INT             NOT NULL,
        ProductId           INT             NOT NULL,
        BatchId             BIGINT          NOT NULL,
        SalesDetailId       BIGINT          NULL,          -- original line, when known

        Quantity            DECIMAL(18,3)   NOT NULL,
        UnitId              INT             NOT NULL,
        Rate                DECIMAL(18,4)   NOT NULL,
        GrossAmount         AS (CAST(Quantity * Rate AS DECIMAL(18,2))) PERSISTED,
        DiscountAmount      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturnDetails_Disc DEFAULT (0),
        TaxableAmount       AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount) PERSISTED,
        GstPercent          DECIMAL(6,3)    NOT NULL CONSTRAINT DF_SalesReturnDetails_GstPct DEFAULT (0),
        CgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturnDetails_Cgst DEFAULT (0),
        SgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturnDetails_Sgst DEFAULT (0),
        IgstAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturnDetails_Igst DEFAULT (0),
        CessAmount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_SalesReturnDetails_Cess DEFAULT (0),
        LineTotal           AS (CAST(Quantity * Rate AS DECIMAL(18,2)) - DiscountAmount
                                + CgstAmount + SgstAmount + IgstAmount + CessAmount) PERSISTED,
        -- Cost the goods return to stock at - the same cost they left at, so
        -- a sale-then-return pair nets to zero profit rather than a phantom gain.
        CostRate            DECIMAL(18,4)   NOT NULL CONSTRAINT DF_SalesReturnDetails_CostRate DEFAULT (0),
        CostAmount          AS (CAST(Quantity * CostRate AS DECIMAL(18,2))) PERSISTED,

        -- Expired or damaged goods come back but must not go back on the shelf.
        IsSaleable          BIT             NOT NULL CONSTRAINT DF_SalesReturnDetails_IsSaleable DEFAULT (1),
        ReturnReason        NVARCHAR(300)   NULL,

        CONSTRAINT PK_SalesReturnDetails PRIMARY KEY CLUSTERED (SalesReturnDetailId),
        CONSTRAINT FK_SalesReturnDetails_Return
            FOREIGN KEY (SalesReturnId) REFERENCES SalesReturns (SalesReturnId) ON DELETE CASCADE,
        CONSTRAINT FK_SalesReturnDetails_Product
            FOREIGN KEY (ProductId)     REFERENCES Products (ProductId),
        CONSTRAINT FK_SalesReturnDetails_Batch
            FOREIGN KEY (BatchId)       REFERENCES ProductBatches (BatchId),
        CONSTRAINT FK_SalesReturnDetails_SalesDetail
            FOREIGN KEY (SalesDetailId) REFERENCES SalesDetails (SalesDetailId),
        CONSTRAINT FK_SalesReturnDetails_Unit
            FOREIGN KEY (UnitId)        REFERENCES Units (UnitId),
        CONSTRAINT CK_SalesReturnDetails_Quantity CHECK (Quantity > 0)
    );

    CREATE NONCLUSTERED INDEX IX_SalesReturnDetails_ReturnId
        ON SalesReturnDetails (SalesReturnId, LineNumber);

    CREATE NONCLUSTERED INDEX IX_SalesReturnDetails_ProductId
        ON SalesReturnDetails (ProductId) INCLUDE (BatchId, Quantity, Rate, CostRate);
END
GO

PRINT N'07_Sales.sql completed.';
GO
