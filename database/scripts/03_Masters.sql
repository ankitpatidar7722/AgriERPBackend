/*==============================================================================
  AgriERP  |  03_Masters.sql
  ------------------------------------------------------------------------------
  Reference and party masters: states, units, tax slabs, HSN, categories,
  manufacturers, suppliers, customers, storage locations.

  Design notes
  ------------
  * States carries the official GST state code. Whether a bill is
    CGST+SGST or IGST is decided by comparing the shop's state to the party's
    state, so this must be a real table, not free text.
  * Categories is self-referencing. "Seeds" is the parent of "Vegetable
    Seeds", "Field Crop Seeds", "Flower Seeds" and "Fruit Seeds" - which is
    exactly the breakdown you listed, without a second table.
  * Opening balance is stored on the party, but the CURRENT outstanding is a
    view (10_Views.sql). A stored balance drifts the moment a bill is edited
    or a payment is reversed; a derived one cannot.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* States                                                                  */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'States', N'U') IS NULL
BEGIN
    CREATE TABLE States
    (
        StateId     INT             NOT NULL,      -- = numeric GST state code
        StateCode   CHAR(2)         NOT NULL,      -- '27' Maharashtra, '24' Gujarat
        StateName   NVARCHAR(60)    NOT NULL,
        StateAbbr   NVARCHAR(5)     NULL,          -- 'MH', 'GJ'
        IsUnionTerritory BIT        NOT NULL CONSTRAINT DF_States_IsUnionTerritory DEFAULT (0),
        IsActive    BIT             NOT NULL CONSTRAINT DF_States_IsActive DEFAULT (1),
        CONSTRAINT PK_States PRIMARY KEY CLUSTERED (StateId),
        CONSTRAINT UQ_States_StateCode UNIQUE (StateCode),
        CONSTRAINT UQ_States_StateName UNIQUE (StateName)
    );
END
GO

/*----------------------------------------------------------------------------*/
/* Units  - units of measure                                               */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Units', N'U') IS NULL
BEGIN
    CREATE TABLE Units
    (
        UnitId      INT             IDENTITY(1,1) NOT NULL,
        UnitCode    NVARCHAR(10)    NOT NULL,      -- KG, GM, LTR, ML, PKT, BAG, NOS
        UnitName    NVARCHAR(50)    NOT NULL,
        Description NVARCHAR(150)   NULL,
        -- Seeds sell in 0.5 kg; bags of urea do not sell in fractions.
        AllowDecimal BIT            NOT NULL CONSTRAINT DF_Units_AllowDecimal DEFAULT (1),
        DisplayOrder INT            NOT NULL CONSTRAINT DF_Units_DisplayOrder DEFAULT (0),
        IsActive    BIT             NOT NULL CONSTRAINT DF_Units_IsActive   DEFAULT (1),
        IsDeleted   BIT             NOT NULL CONSTRAINT DF_Units_IsDeleted  DEFAULT (0),
        CreatedAt   DATETIME2(3)    NOT NULL CONSTRAINT DF_Units_CreatedAt  DEFAULT (SYSUTCDATETIME()),
        CreatedBy   INT             NULL,
        UpdatedAt   DATETIME2(3)    NULL,
        UpdatedBy   INT             NULL,
        RowVersion  ROWVERSION      NOT NULL,
        CONSTRAINT PK_Units PRIMARY KEY CLUSTERED (UnitId)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Units_UnitCode
        ON Units (UnitCode) WHERE IsDeleted = 0;
END
GO

/*----------------------------------------------------------------------------*/
/* GstSlabs                                                                */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'GstSlabs', N'U') IS NULL
BEGIN
    CREATE TABLE GstSlabs
    (
        GstSlabId       INT             IDENTITY(1,1) NOT NULL,
        SlabName        NVARCHAR(30)    NOT NULL,      -- 'GST 18%'
        TotalRate       DECIMAL(6,3)    NOT NULL,      -- 18.000
        CgstRate        DECIMAL(6,3)    NOT NULL,      --  9.000  (intra-state)
        SgstRate        DECIMAL(6,3)    NOT NULL,      --  9.000  (intra-state)
        IgstRate        DECIMAL(6,3)    NOT NULL,      -- 18.000  (inter-state)
        CessRate        DECIMAL(6,3)    NOT NULL CONSTRAINT DF_GstSlabs_CessRate DEFAULT (0),
        EffectiveFrom   DATE            NOT NULL CONSTRAINT DF_GstSlabs_EffectiveFrom DEFAULT ('2017-07-01'),
        IsActive        BIT             NOT NULL CONSTRAINT DF_GstSlabs_IsActive DEFAULT (1),
        CONSTRAINT PK_GstSlabs PRIMARY KEY CLUSTERED (GstSlabId),
        CONSTRAINT UQ_GstSlabs_TotalRate UNIQUE (TotalRate),
        -- Guards against a half-entered slab silently under-charging tax.
        CONSTRAINT CK_GstSlabs_Split CHECK (CgstRate + SgstRate = TotalRate AND IgstRate = TotalRate),
        CONSTRAINT CK_GstSlabs_Range CHECK (TotalRate >= 0 AND TotalRate <= 100)
    );
END
GO

/*----------------------------------------------------------------------------*/
/* HsnCodes                                                                */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'HsnCodes', N'U') IS NULL
BEGIN
    CREATE TABLE HsnCodes
    (
        HsnId           INT             IDENTITY(1,1) NOT NULL,
        HsnCode         NVARCHAR(10)    NOT NULL,      -- '3808', '3102', '1209'
        Description     NVARCHAR(250)   NOT NULL,
        DefaultGstSlabId INT            NULL,
        IsActive        BIT             NOT NULL CONSTRAINT DF_HsnCodes_IsActive DEFAULT (1),
        CONSTRAINT PK_HsnCodes PRIMARY KEY CLUSTERED (HsnId),
        CONSTRAINT UQ_HsnCodes_HsnCode UNIQUE (HsnCode),
        CONSTRAINT FK_HsnCodes_GstSlab
            FOREIGN KEY (DefaultGstSlabId) REFERENCES GstSlabs (GstSlabId)
    );
END
GO

/*----------------------------------------------------------------------------*/
/* Categories  - self-referencing hierarchy                                */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Categories', N'U') IS NULL
BEGIN
    CREATE TABLE Categories
    (
        CategoryId          INT             IDENTITY(1,1) NOT NULL,
        CategoryCode        NVARCHAR(20)    NOT NULL,
        CategoryName        NVARCHAR(100)   NOT NULL,
        ParentCategoryId    INT             NULL,
        Description         NVARCHAR(300)   NULL,
        -- Nudges the UI: seeds and fertilizers behave differently on a bill.
        IconName            NVARCHAR(40)    NULL,
        DisplayOrder        INT             NOT NULL CONSTRAINT DF_Categories_DisplayOrder DEFAULT (0),
        IsActive            BIT             NOT NULL CONSTRAINT DF_Categories_IsActive  DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_Categories_IsDeleted DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Categories_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,
        CONSTRAINT PK_Categories PRIMARY KEY CLUSTERED (CategoryId),
        -- NO ACTION: a parent with children must not be deletable.
        CONSTRAINT FK_Categories_Parent
            FOREIGN KEY (ParentCategoryId) REFERENCES Categories (CategoryId)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Categories_Code
        ON Categories (CategoryCode) WHERE IsDeleted = 0;

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Categories_Name
        ON Categories (CategoryName) WHERE IsDeleted = 0;

    CREATE NONCLUSTERED INDEX IX_Categories_ParentCategoryId
        ON Categories (ParentCategoryId) INCLUDE (CategoryName, DisplayOrder)
        WHERE IsDeleted = 0;
END
GO

/*----------------------------------------------------------------------------*/
/* Companies  - manufacturers (UPL, Bayer, Syngenta, IFFCO, ...)           */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Companies', N'U') IS NULL
BEGIN
    CREATE TABLE Companies
    (
        CompanyId       INT             IDENTITY(1,1) NOT NULL,
        CompanyCode     NVARCHAR(20)    NOT NULL,
        CompanyName     NVARCHAR(150)   NOT NULL,
        GstNumber       VARCHAR(15)     NULL,
        Address         NVARCHAR(300)   NULL,
        City            NVARCHAR(80)    NULL,
        StateId         INT             NULL,
        Pincode         VARCHAR(6)      NULL,
        Phone           NVARCHAR(15)    NULL,
        Email           NVARCHAR(150)   NULL,
        Website         NVARCHAR(200)   NULL,
        ContactPerson   NVARCHAR(120)   NULL,
        LogoPath        NVARCHAR(300)   NULL,
        Remarks         NVARCHAR(500)   NULL,
        IsActive        BIT             NOT NULL CONSTRAINT DF_Companies_IsActive  DEFAULT (1),
        IsDeleted       BIT             NOT NULL CONSTRAINT DF_Companies_IsDeleted DEFAULT (0),
        CreatedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_Companies_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        UpdatedBy       INT             NULL,
        RowVersion      ROWVERSION      NOT NULL,
        CONSTRAINT PK_Companies PRIMARY KEY CLUSTERED (CompanyId),
        CONSTRAINT FK_Companies_State FOREIGN KEY (StateId) REFERENCES States (StateId),
        CONSTRAINT CK_Companies_Pincode CHECK (Pincode IS NULL OR Pincode LIKE '[1-9][0-9][0-9][0-9][0-9][0-9]')
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Companies_Code
        ON Companies (CompanyCode) WHERE IsDeleted = 0;

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Companies_Name
        ON Companies (CompanyName) WHERE IsDeleted = 0;
END
GO

/*----------------------------------------------------------------------------*/
/* Suppliers                                                               */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Suppliers', N'U') IS NULL
BEGIN
    CREATE TABLE Suppliers
    (
        SupplierId          INT             IDENTITY(1,1) NOT NULL,
        SupplierCode        NVARCHAR(20)    NOT NULL,
        SupplierName        NVARCHAR(150)   NOT NULL,
        GstNumber           VARCHAR(15)     NULL,
        PanNumber           VARCHAR(10)     NULL,
        Address             NVARCHAR(300)   NULL,
        City                NVARCHAR(80)    NULL,
        StateId             INT             NULL,
        Pincode             VARCHAR(6)      NULL,
        Phone               NVARCHAR(15)    NULL,
        AlternatePhone      NVARCHAR(15)    NULL,
        Email               NVARCHAR(150)   NULL,
        ContactPerson       NVARCHAR(120)   NULL,

        PaymentTermDays     INT             NOT NULL CONSTRAINT DF_Suppliers_PaymentTermDays DEFAULT (0),
        CreditLimit         DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Suppliers_CreditLimit     DEFAULT (0),
        -- Balance carried in when the shop started using this system.
        -- 'CR' = we owe the supplier (the normal case).
        OpeningBalance      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Suppliers_OpeningBalance  DEFAULT (0),
        OpeningBalanceType  CHAR(2)         NOT NULL CONSTRAINT DF_Suppliers_OpeningBalanceType DEFAULT ('CR'),
        OpeningBalanceDate  DATE            NULL,

        BankName            NVARCHAR(120)   NULL,
        BankAccountNumber   NVARCHAR(30)    NULL,
        BankIfsc            VARCHAR(11)     NULL,

        Remarks             NVARCHAR(500)   NULL,
        IsActive            BIT             NOT NULL CONSTRAINT DF_Suppliers_IsActive  DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_Suppliers_IsDeleted DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Suppliers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_Suppliers PRIMARY KEY CLUSTERED (SupplierId),
        CONSTRAINT FK_Suppliers_State FOREIGN KEY (StateId) REFERENCES States (StateId),
        CONSTRAINT CK_Suppliers_OpeningBalanceType CHECK (OpeningBalanceType IN ('DR','CR')),
        CONSTRAINT CK_Suppliers_PaymentTermDays    CHECK (PaymentTermDays >= 0),
        CONSTRAINT CK_Suppliers_Pincode CHECK (Pincode IS NULL OR Pincode LIKE '[1-9][0-9][0-9][0-9][0-9][0-9]')
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Suppliers_Code
        ON Suppliers (SupplierCode) WHERE IsDeleted = 0;

    -- Two suppliers may share a trade name; a GSTIN is unique by law.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Suppliers_GstNumber
        ON Suppliers (GstNumber) WHERE IsDeleted = 0 AND GstNumber IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_Suppliers_Name
        ON Suppliers (SupplierName) INCLUDE (Phone, City, IsActive) WHERE IsDeleted = 0;
END
GO

/*----------------------------------------------------------------------------*/
/* Customers                                                               */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Customers', N'U') IS NULL
BEGIN
    CREATE TABLE Customers
    (
        CustomerId          INT             IDENTITY(1,1) NOT NULL,
        CustomerCode        NVARCHAR(20)    NOT NULL,
        CustomerName        NVARCHAR(150)   NOT NULL,
        -- The single most-used search field in a village agri shop.
        Village             NVARCHAR(100)   NULL,
        Mobile              NVARCHAR(15)    NULL,
        AlternateMobile     NVARCHAR(15)    NULL,
        GstNumber           VARCHAR(15)     NULL,
        Address             NVARCHAR(300)   NULL,
        City                NVARCHAR(80)    NULL,
        StateId             INT             NULL,
        Pincode             VARCHAR(6)      NULL,

        -- Decides which of the four product rates the billing screen defaults to.
        CustomerType        NVARCHAR(15)    NOT NULL CONSTRAINT DF_Customers_CustomerType DEFAULT ('Retail'),
        CreditLimit         DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Customers_CreditLimit    DEFAULT (0),
        CreditDays          INT             NOT NULL CONSTRAINT DF_Customers_CreditDays     DEFAULT (0),
        OpeningBalance      DECIMAL(18,2)   NOT NULL CONSTRAINT DF_Customers_OpeningBalance DEFAULT (0),
        -- 'DR' = customer owes the shop (the normal case).
        OpeningBalanceType  CHAR(2)         NOT NULL CONSTRAINT DF_Customers_OpeningBalanceType DEFAULT ('DR'),
        OpeningBalanceDate  DATE            NULL,

        Remarks             NVARCHAR(500)   NULL,
        IsActive            BIT             NOT NULL CONSTRAINT DF_Customers_IsActive  DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerId),
        CONSTRAINT FK_Customers_State FOREIGN KEY (StateId) REFERENCES States (StateId),
        CONSTRAINT CK_Customers_CustomerType CHECK (CustomerType IN ('Retail','Wholesale','Dealer')),
        CONSTRAINT CK_Customers_OpeningBalanceType CHECK (OpeningBalanceType IN ('DR','CR')),
        CONSTRAINT CK_Customers_CreditDays CHECK (CreditDays >= 0),
        CONSTRAINT CK_Customers_Pincode CHECK (Pincode IS NULL OR Pincode LIKE '[1-9][0-9][0-9][0-9][0-9][0-9]')
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Customers_Code
        ON Customers (CustomerCode) WHERE IsDeleted = 0;

    -- Mobile is how the counter looks a farmer up. Unique, but optional:
    -- pure cash walk-ins are billed without being created as a customer.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Customers_Mobile
        ON Customers (Mobile) WHERE IsDeleted = 0 AND Mobile IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_Customers_Name
        ON Customers (CustomerName) INCLUDE (Village, Mobile, IsActive) WHERE IsDeleted = 0;

    CREATE NONCLUSTERED INDEX IX_Customers_Village
        ON Customers (Village) INCLUDE (CustomerName, Mobile) WHERE IsDeleted = 0;
END
GO

/*----------------------------------------------------------------------------*/
/* StorageLocations  - godown / rack hierarchy                             */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'StorageLocations', N'U') IS NULL
BEGIN
    CREATE TABLE StorageLocations
    (
        LocationId          INT             IDENTITY(1,1) NOT NULL,
        LocationCode        NVARCHAR(20)    NOT NULL,
        LocationName        NVARCHAR(100)   NOT NULL,
        LocationType        NVARCHAR(20)    NOT NULL CONSTRAINT DF_StorageLocations_Type DEFAULT ('Rack'),
        ParentLocationId    INT             NULL,
        -- Exactly one default location; stock entries omit it and land here.
        IsDefault           BIT             NOT NULL CONSTRAINT DF_StorageLocations_IsDefault DEFAULT (0),
        Remarks             NVARCHAR(300)   NULL,
        IsActive            BIT             NOT NULL CONSTRAINT DF_StorageLocations_IsActive  DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_StorageLocations_IsDeleted DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_StorageLocations_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,
        CONSTRAINT PK_StorageLocations PRIMARY KEY CLUSTERED (LocationId),
        CONSTRAINT FK_StorageLocations_Parent
            FOREIGN KEY (ParentLocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT CK_StorageLocations_Type CHECK (LocationType IN ('Warehouse','Godown','Rack','Shelf','Counter'))
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_StorageLocations_Code
        ON StorageLocations (LocationCode) WHERE IsDeleted = 0;

    CREATE UNIQUE NONCLUSTERED INDEX UQ_StorageLocations_Default
        ON StorageLocations (IsDefault) WHERE IsDefault = 1 AND IsDeleted = 0;
END
GO

PRINT N'03_Masters.sql completed.';
GO
