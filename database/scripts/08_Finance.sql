/*==============================================================================
  AgriERP  |  08_Finance.sql
  ------------------------------------------------------------------------------
  Payment modes, party payments with invoice-wise allocation, and expenses.

  ONE PAYMENT TABLE, NOT TWO
  --------------------------
  Money received from a farmer and money paid to a distributor are the same
  event with opposite signs, and both need the same features: allocation across
  invoices, on-account balances, cheque tracking, cancellation. Two near-identical
  tables would mean writing the ageing logic twice and fixing every bug twice.
  So there is one Payments, keyed by PartyType (Customer / Supplier) and
  PaymentType (Receipt = money in, Payment = money out). All four combinations
  are legitimate - a supplier refunding a rejected consignment is Supplier +
  Receipt - and CK_Payments_Party guarantees exactly one party column is filled.

  ALLOCATION IS A SEPARATE TABLE
  ------------------------------
  A farmer hands over 5,000 against three old bills. PaymentAllocations
  records how that 5,000 was split, which is what makes bill-wise outstanding
  and ageing possible. Anything not allocated stays as UnallocatedAmount - a
  genuine on-account advance - instead of being silently spread around.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* PaymentModes                                                            */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'PaymentModes', N'U') IS NULL
BEGIN
    CREATE TABLE PaymentModes
    (
        PaymentModeId   INT             IDENTITY(1,1) NOT NULL,
        ModeCode        NVARCHAR(20)    NOT NULL,      -- CASH, UPI, CARD, CHEQUE, NEFT
        ModeName        NVARCHAR(50)    NOT NULL,
        -- Cheques and NEFT need a reference number; cash does not.
        RequiresReference BIT           NOT NULL CONSTRAINT DF_PaymentModes_RequiresReference DEFAULT (0),
        IsBankMode      BIT             NOT NULL CONSTRAINT DF_PaymentModes_IsBankMode DEFAULT (0),
        DisplayOrder    INT             NOT NULL CONSTRAINT DF_PaymentModes_DisplayOrder DEFAULT (0),
        IsActive        BIT             NOT NULL CONSTRAINT DF_PaymentModes_IsActive DEFAULT (1),
        CONSTRAINT PK_PaymentModes PRIMARY KEY CLUSTERED (PaymentModeId),
        CONSTRAINT UQ_PaymentModes_Code UNIQUE (ModeCode)
    );
END
GO

-- Deferred from 07_Sales.sql: SalePayments was created before this table.
-- The existence check goes through sys.foreign_keys rather than OBJECT_ID.
-- A constraint lives in its table's schema, so OBJECT_ID(N'FK_...') resolves
-- against the CALLER's default schema (dbo for a SQL login) and silently
-- returns NULL - making this block try to re-create the FK on every re-run.
IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_SalePayments_PaymentMode'
      AND parent_object_id = OBJECT_ID(N'SalePayments', N'U')
)
AND OBJECT_ID(N'SalePayments', N'U') IS NOT NULL
BEGIN
    ALTER TABLE SalePayments WITH CHECK
        ADD CONSTRAINT FK_SalePayments_PaymentMode
        FOREIGN KEY (PaymentModeId) REFERENCES PaymentModes (PaymentModeId);
END
GO

/*----------------------------------------------------------------------------*/
/* Payments                                                                */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Payments', N'U') IS NULL
BEGIN
    CREATE TABLE Payments
    (
        PaymentId           BIGINT          IDENTITY(1,1) NOT NULL,
        VoucherNumber       NVARCHAR(30)    NOT NULL,
        PaymentDate         DATE            NOT NULL,

        PartyType           NVARCHAR(10)    NOT NULL,      -- Customer | Supplier
        CustomerId          INT             NULL,
        SupplierId          INT             NULL,
        PaymentType         NVARCHAR(10)    NOT NULL,      -- Receipt (in) | Payment (out)

        PaymentModeId       INT             NOT NULL,
        Amount              DECIMAL(18,2)   NOT NULL,
        -- Maintained as allocations are added; the remainder sits on account.
        AllocatedAmount     DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Payments_AllocatedAmount DEFAULT (0),
        UnallocatedAmount   AS (Amount - AllocatedAmount) PERSISTED,

        ReferenceNumber     NVARCHAR(60)    NULL,          -- UPI ref / cheque no / UTR
        BankName            NVARCHAR(120)   NULL,
        ChequeDate          DATE            NULL,
        -- Pending -> Cleared / Bounced. A bounced cheque must reopen the bill.
        ClearanceStatus     NVARCHAR(15)    NOT NULL CONSTRAINT DF_Payments_ClearanceStatus DEFAULT ('Cleared'),
        ClearedDate         DATE            NULL,

        Remarks             NVARCHAR(500)   NULL,
        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_Payments_Status DEFAULT ('Posted'),
        CancelledAt         DATETIME2(3)    NULL,
        CancelledBy         INT             NULL,
        CancelReason        NVARCHAR(300)   NULL,

        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_Payments PRIMARY KEY CLUSTERED (PaymentId),
        CONSTRAINT UQ_Payments_VoucherNumber UNIQUE (VoucherNumber),
        CONSTRAINT FK_Payments_Customer
            FOREIGN KEY (CustomerId)    REFERENCES Customers (CustomerId),
        CONSTRAINT FK_Payments_Supplier
            FOREIGN KEY (SupplierId)    REFERENCES Suppliers (SupplierId),
        CONSTRAINT FK_Payments_Mode
            FOREIGN KEY (PaymentModeId) REFERENCES PaymentModes (PaymentModeId),

        CONSTRAINT CK_Payments_PartyType   CHECK (PartyType   IN ('Customer','Supplier')),
        CONSTRAINT CK_Payments_PaymentType CHECK (PaymentType IN ('Receipt','Payment')),
        CONSTRAINT CK_Payments_Status      CHECK (Status      IN ('Posted','Cancelled')),
        CONSTRAINT CK_Payments_Clearance   CHECK (ClearanceStatus IN ('Pending','Cleared','Bounced')),
        -- Exactly one party, and it must match PartyType.
        CONSTRAINT CK_Payments_Party CHECK (
            (PartyType = 'Customer' AND CustomerId IS NOT NULL AND SupplierId IS NULL) OR
            (PartyType = 'Supplier' AND SupplierId IS NOT NULL AND CustomerId IS NULL)),
        CONSTRAINT CK_Payments_Amount CHECK (Amount > 0),
        -- Cannot allocate more than was received.
        CONSTRAINT CK_Payments_Allocated CHECK (AllocatedAmount >= 0 AND AllocatedAmount <= Amount)
    );

    CREATE NONCLUSTERED INDEX IX_Payments_Customer_Date
        ON Payments (CustomerId, PaymentDate DESC)
        INCLUDE (VoucherNumber, Amount, AllocatedAmount, UnallocatedAmount, PaymentType, Status)
        WHERE CustomerId IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_Payments_Supplier_Date
        ON Payments (SupplierId, PaymentDate DESC)
        INCLUDE (VoucherNumber, Amount, AllocatedAmount, UnallocatedAmount, PaymentType, Status)
        WHERE SupplierId IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_Payments_Date
        ON Payments (PaymentDate) INCLUDE (PaymentModeId, PaymentType, Amount, Status);

    -- Cheques awaiting clearance.
    CREATE NONCLUSTERED INDEX IX_Payments_Pending
        ON Payments (ClearanceStatus, ChequeDate)
        INCLUDE (VoucherNumber, Amount, CustomerId, SupplierId)
        WHERE ClearanceStatus = 'Pending';
END
GO

/*----------------------------------------------------------------------------*/
/* PaymentAllocations                                                      */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'PaymentAllocations', N'U') IS NULL
BEGIN
    CREATE TABLE PaymentAllocations
    (
        PaymentAllocationId BIGINT          IDENTITY(1,1) NOT NULL,
        PaymentId           BIGINT          NOT NULL,
        -- Polymorphic, like the stock journal: one allocation table serves
        -- invoices and credit/debit notes alike.
        ReferenceType       NVARCHAR(20)    NOT NULL,      -- Sale | Purchase | SalesReturn | PurchaseReturn
        ReferenceId         BIGINT          NOT NULL,
        ReferenceNumber     NVARCHAR(30)    NULL,
        AllocatedAmount     DECIMAL(18,2)   NOT NULL,
        AllocatedAt         DATETIME2(3)    NOT NULL CONSTRAINT DF_PaymentAllocations_AllocatedAt DEFAULT (SYSUTCDATETIME()),
        AllocatedBy         INT             NULL,

        CONSTRAINT PK_PaymentAllocations PRIMARY KEY CLUSTERED (PaymentAllocationId),
        CONSTRAINT FK_PaymentAllocations_Payment
            FOREIGN KEY (PaymentId) REFERENCES Payments (PaymentId) ON DELETE CASCADE,
        CONSTRAINT CK_PaymentAllocations_ReferenceType
            CHECK (ReferenceType IN ('Sale','Purchase','SalesReturn','PurchaseReturn')),
        CONSTRAINT CK_PaymentAllocations_Amount CHECK (AllocatedAmount > 0),
        -- One payment settles a given bill once; a second instalment is a
        -- second payment, not a second allocation row on the same one.
        CONSTRAINT UQ_PaymentAllocations_Payment_Reference
            UNIQUE (PaymentId, ReferenceType, ReferenceId)
    );

    -- "What has been received against invoice X" - drives BalanceAmount upkeep.
    CREATE NONCLUSTERED INDEX IX_PaymentAllocations_Reference
        ON PaymentAllocations (ReferenceType, ReferenceId)
        INCLUDE (PaymentId, AllocatedAmount, AllocatedAt);
END
GO

/*----------------------------------------------------------------------------*/
/* ExpenseCategories                                                       */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'ExpenseCategories', N'U') IS NULL
BEGIN
    CREATE TABLE ExpenseCategories
    (
        ExpenseCategoryId   INT             IDENTITY(1,1) NOT NULL,
        CategoryCode        NVARCHAR(20)    NOT NULL,
        CategoryName        NVARCHAR(100)   NOT NULL,
        Description         NVARCHAR(300)   NULL,
        IsActive            BIT             NOT NULL CONSTRAINT DF_ExpenseCategories_IsActive DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_ExpenseCategories_IsDeleted DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_ExpenseCategories_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,
        CONSTRAINT PK_ExpenseCategories PRIMARY KEY CLUSTERED (ExpenseCategoryId)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_ExpenseCategories_Code
        ON ExpenseCategories (CategoryCode) WHERE IsDeleted = 0;
END
GO

/*----------------------------------------------------------------------------*/
/* Expenses                                                                */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Expenses', N'U') IS NULL
BEGIN
    CREATE TABLE Expenses
    (
        ExpenseId           BIGINT          IDENTITY(1,1) NOT NULL,
        VoucherNumber       NVARCHAR(30)    NOT NULL,
        ExpenseDate         DATE            NOT NULL,
        ExpenseCategoryId   INT             NOT NULL,
        PaymentModeId       INT             NOT NULL,
        PaidTo              NVARCHAR(150)   NULL,
        Amount              DECIMAL(18,2)   NOT NULL,
        -- Input credit on a GST expense bill; 0 for wages, tea, and the like.
        GstAmount           DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Expenses_GstAmount DEFAULT (0),
        TotalAmount         AS (Amount + GstAmount) PERSISTED,
        ReferenceNumber     NVARCHAR(60)    NULL,
        BillNumber          NVARCHAR(50)    NULL,
        AttachmentPath      NVARCHAR(300)   NULL,
        Description         NVARCHAR(500)   NULL,

        Status              NVARCHAR(15)    NOT NULL CONSTRAINT DF_Expenses_Status DEFAULT ('Posted'),
        FinancialYearId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Expenses_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_Expenses PRIMARY KEY CLUSTERED (ExpenseId),
        CONSTRAINT UQ_Expenses_VoucherNumber UNIQUE (VoucherNumber),
        CONSTRAINT FK_Expenses_Category
            FOREIGN KEY (ExpenseCategoryId) REFERENCES ExpenseCategories (ExpenseCategoryId),
        CONSTRAINT FK_Expenses_PaymentMode
            FOREIGN KEY (PaymentModeId)     REFERENCES PaymentModes (PaymentModeId),
        CONSTRAINT CK_Expenses_Amount CHECK (Amount > 0 AND GstAmount >= 0),
        CONSTRAINT CK_Expenses_Status CHECK (Status IN ('Posted','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_Expenses_Date
        ON Expenses (ExpenseDate) INCLUDE (ExpenseCategoryId, Amount, GstAmount, TotalAmount, Status);

    CREATE NONCLUSTERED INDEX IX_Expenses_Category
        ON Expenses (ExpenseCategoryId, ExpenseDate DESC) INCLUDE (TotalAmount, Status);
END
GO

PRINT N'08_Finance.sql completed.';
GO
