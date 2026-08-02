/*==============================================================================
  AgriERP  |  04_Products.sql
  ------------------------------------------------------------------------------
  Product master, batches, images and price history.

  WHY BATCH / EXPIRY / STOCK ARE NOT COLUMNS ON Products
  ---------------------------------------------------------
  A single product - say Confidor 17.8% SL 250ml - is bought many times. Each
  purchase arrives with its own batch number, its own expiry date and usually a
  different purchase rate. If BatchNumber and ExpiryDate were columns on the
  product row, then:

      * "which batches expire in 60 days" cannot be answered at all;
      * the second purchase would overwrite the first batch's expiry, so old
        stock silently inherits a new expiry date - a real compliance problem
        for pesticides;
      * batch-wise profit is unknowable because only one purchase rate survives.

  So batches live in ProductBatches, one row per (product, batch, location).
  Stock is held there as InwardQty / OutwardQty with CurrentQty as a PERSISTED
  computed column - it can never disagree with its own inputs. Product-level
  stock is the sum across batches, exposed by vw_ProductStock.

  Products that genuinely have no batches (a sprayer, a khurpi) still get one
  batch row with BatchNumber 'GEN', so every stock path in the application is
  identical and there is no "is it batched?" branch in the billing code.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------*/
/* Products                                                                */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'Products', N'U') IS NULL
BEGIN
    CREATE TABLE Products
    (
        ProductId           INT             IDENTITY(1,1) NOT NULL,
        ProductCode         NVARCHAR(30)    NOT NULL,      -- auto: PRD-000001
        ProductName         NVARCHAR(200)   NOT NULL,      -- 'Confidor 17.8% SL'
        ShortName           NVARCHAR(60)    NULL,          -- shown on the invoice line
        -- Active ingredient. Farmers ask for "imidacloprid", not the brand,
        -- and this is what the counter actually searches on.
        TechnicalName       NVARCHAR(200)   NULL,          -- 'Imidacloprid 17.8% SL'

        CategoryId          INT             NOT NULL,
        CompanyId           INT             NULL,          -- manufacturer
        Brand               NVARCHAR(100)   NULL,

        -- Packing: 250 + ML renders as "250 ML" on screen and in reports.
        PackingSize         DECIMAL(18,3)   NULL,
        PackingUnitId       INT             NULL,
        UnitId              INT             NOT NULL,      -- unit the product SELLS in

        HsnId               INT             NULL,
        GstSlabId           INT             NOT NULL,
        -- 1 = the rates below already contain GST (common for MRP-billed items).
        IsRateInclusiveOfTax BIT            NOT NULL CONSTRAINT DF_Products_IsRateInclusiveOfTax DEFAULT (0),

        /* --- Pricing. DECIMAL(18,4): agri rates carry paise-level precision --- */
        PurchaseRate        DECIMAL(18,4)   NOT NULL CONSTRAINT DF_Products_PurchaseRate     DEFAULT (0),
        SellingRate         DECIMAL(18,4)   NOT NULL CONSTRAINT DF_Products_SellingRate      DEFAULT (0),
        Mrp                 DECIMAL(18,4)   NOT NULL CONSTRAINT DF_Products_Mrp              DEFAULT (0),
        WholesaleRate       DECIMAL(18,4)   NOT NULL CONSTRAINT DF_Products_WholesaleRate    DEFAULT (0),
        DealerRate          DECIMAL(18,4)   NOT NULL CONSTRAINT DF_Products_DealerRate       DEFAULT (0),
        -- Floor price. The billing screen refuses to go below this without the
        -- Sales.OverrideMinRate permission.
        MinSellingRate      DECIMAL(18,4)   NOT NULL CONSTRAINT DF_Products_MinSellingRate   DEFAULT (0),

        /* --- Stock policy. Quantities are DECIMAL(18,3) throughout --- */
        MinStockLevel       DECIMAL(18,3)   NOT NULL CONSTRAINT DF_Products_MinStockLevel    DEFAULT (0),
        MaxStockLevel       DECIMAL(18,3)   NOT NULL CONSTRAINT DF_Products_MaxStockLevel    DEFAULT (0),
        ReorderLevel        DECIMAL(18,3)   NOT NULL CONSTRAINT DF_Products_ReorderLevel     DEFAULT (0),

        IsBatchTracked      BIT             NOT NULL CONSTRAINT DF_Products_IsBatchTracked   DEFAULT (1),
        IsExpiryTracked     BIT             NOT NULL CONSTRAINT DF_Products_IsExpiryTracked  DEFAULT (1),
        -- Off by default: selling stock you do not have corrupts valuation.
        AllowNegativeStock  BIT             NOT NULL CONSTRAINT DF_Products_AllowNegativeStock DEFAULT (0),

        DefaultLocationId   INT             NULL,
        RackNumber          NVARCHAR(30)    NULL,          -- printed on the picking slip
        Barcode             NVARCHAR(50)    NULL,

        ImagePath           NVARCHAR(300)   NULL,
        Description         NVARCHAR(1000)  NULL,
        -- Pesticide licence / CIB registration number, kept for inspections.
        LicenceNumber       NVARCHAR(50)    NULL,

        IsActive            BIT             NOT NULL CONSTRAINT DF_Products_IsActive  DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_Products_IsDeleted DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Products_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_Products PRIMARY KEY CLUSTERED (ProductId),
        CONSTRAINT FK_Products_Category    FOREIGN KEY (CategoryId)        REFERENCES Categories (CategoryId),
        CONSTRAINT FK_Products_Company     FOREIGN KEY (CompanyId)         REFERENCES Companies (CompanyId),
        CONSTRAINT FK_Products_Unit        FOREIGN KEY (UnitId)            REFERENCES Units (UnitId),
        CONSTRAINT FK_Products_PackingUnit FOREIGN KEY (PackingUnitId)     REFERENCES Units (UnitId),
        CONSTRAINT FK_Products_Hsn         FOREIGN KEY (HsnId)             REFERENCES HsnCodes (HsnId),
        CONSTRAINT FK_Products_GstSlab     FOREIGN KEY (GstSlabId)         REFERENCES GstSlabs (GstSlabId),
        CONSTRAINT FK_Products_Location    FOREIGN KEY (DefaultLocationId) REFERENCES StorageLocations (LocationId),

        CONSTRAINT CK_Products_Rates CHECK (
            PurchaseRate >= 0 AND SellingRate >= 0 AND Mrp >= 0 AND
            WholesaleRate >= 0 AND DealerRate >= 0 AND MinSellingRate >= 0),
        CONSTRAINT CK_Products_StockLevels CHECK (
            MinStockLevel >= 0 AND MaxStockLevel >= 0 AND ReorderLevel >= 0 AND
            (MaxStockLevel = 0 OR MaxStockLevel >= MinStockLevel)),
        CONSTRAINT CK_Products_PackingSize CHECK (PackingSize IS NULL OR PackingSize > 0)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_Products_ProductCode
        ON Products (ProductCode) WHERE IsDeleted = 0;

    -- Filtered: most products have no barcode, and NULLs must not collide.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Products_Barcode
        ON Products (Barcode) WHERE IsDeleted = 0 AND Barcode IS NOT NULL;

    -- The same brand in two pack sizes is two products; the same brand, same
    -- pack, entered twice is a duplicate. This catches the second case.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Products_Name_Company_Packing
        ON Products (ProductName, CompanyId, PackingSize, PackingUnitId)
        WHERE IsDeleted = 0;

    -- Covers the product-list grid: filter by category, show name + rates.
    CREATE NONCLUSTERED INDEX IX_Products_CategoryId
        ON Products (CategoryId)
        INCLUDE (ProductName, CompanyId, SellingRate, Mrp, UnitId, IsActive)
        WHERE IsDeleted = 0;

    CREATE NONCLUSTERED INDEX IX_Products_CompanyId
        ON Products (CompanyId)
        INCLUDE (ProductName, CategoryId, SellingRate, IsActive)
        WHERE IsDeleted = 0;

    -- Type-ahead on the billing screen.
    CREATE NONCLUSTERED INDEX IX_Products_ProductName
        ON Products (ProductName)
        INCLUDE (ProductCode, ShortName, SellingRate, Mrp, UnitId, GstSlabId)
        WHERE IsDeleted = 0 AND IsActive = 1;

    CREATE NONCLUSTERED INDEX IX_Products_TechnicalName
        ON Products (TechnicalName) INCLUDE (ProductName, ProductId)
        WHERE IsDeleted = 0 AND IsActive = 1 AND TechnicalName IS NOT NULL;
END
GO

/*----------------------------------------------------------------------------*/
/* ProductBatches  - the single source of truth for on-hand stock          */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'ProductBatches', N'U') IS NULL
BEGIN
    CREATE TABLE ProductBatches
    (
        BatchId             BIGINT          IDENTITY(1,1) NOT NULL,
        ProductId           INT             NOT NULL,
        -- 'GEN' for products that are not genuinely batch-tracked.
        BatchNumber         NVARCHAR(50)    NOT NULL,
        LocationId          INT             NOT NULL,
        ManufacturingDate   DATE            NULL,
        ExpiryDate          DATE            NULL,

        -- Landed cost for THIS batch, used for batch-wise profit and valuation.
        PurchaseRate        DECIMAL(18,4)   NOT NULL CONSTRAINT DF_ProductBatches_PurchaseRate DEFAULT (0),
        Mrp                 DECIMAL(18,4)   NOT NULL CONSTRAINT DF_ProductBatches_Mrp          DEFAULT (0),
        SellingRate         DECIMAL(18,4)   NOT NULL CONSTRAINT DF_ProductBatches_SellingRate  DEFAULT (0),

        -- Running totals maintained by the stock-posting procedure.
        InwardQty           DECIMAL(18,3)   NOT NULL CONSTRAINT DF_ProductBatches_InwardQty  DEFAULT (0),
        OutwardQty          DECIMAL(18,3)   NOT NULL CONSTRAINT DF_ProductBatches_OutwardQty DEFAULT (0),
        -- PERSISTED: derived, indexable, and structurally unable to drift.
        CurrentQty          AS (InwardQty - OutwardQty) PERSISTED,

        Remarks             NVARCHAR(300)   NULL,
        IsActive            BIT             NOT NULL CONSTRAINT DF_ProductBatches_IsActive DEFAULT (1),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_ProductBatches_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_ProductBatches PRIMARY KEY CLUSTERED (BatchId),
        CONSTRAINT FK_ProductBatches_Product
            FOREIGN KEY (ProductId)  REFERENCES Products (ProductId),
        CONSTRAINT FK_ProductBatches_Location
            FOREIGN KEY (LocationId) REFERENCES StorageLocations (LocationId),
        CONSTRAINT CK_ProductBatches_Qty CHECK (InwardQty >= 0 AND OutwardQty >= 0),
        CONSTRAINT CK_ProductBatches_Dates
            CHECK (ManufacturingDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate >= ManufacturingDate)
    );

    -- One physical lot per product+batch+location. Re-purchasing the same batch
    -- adds to this row rather than creating a second one.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_ProductBatches_Product_Batch_Location
        ON ProductBatches (ProductId, BatchNumber, LocationId);

    -- FEFO picking: earliest-expiring batch that still has stock.
    CREATE NONCLUSTERED INDEX IX_ProductBatches_Product_Expiry
        ON ProductBatches (ProductId, ExpiryDate)
        INCLUDE (BatchId, BatchNumber, CurrentQty, PurchaseRate, Mrp, LocationId);

    -- Near-expiry and expired-stock reports scan by date across all products.
    CREATE NONCLUSTERED INDEX IX_ProductBatches_ExpiryDate
        ON ProductBatches (ExpiryDate)
        INCLUDE (ProductId, BatchNumber, CurrentQty, PurchaseRate)
        WHERE ExpiryDate IS NOT NULL;

    CREATE NONCLUSTERED INDEX IX_ProductBatches_LocationId
        ON ProductBatches (LocationId) INCLUDE (ProductId, CurrentQty);
END
GO

/*----------------------------------------------------------------------------*/
/* ProductImages                                                           */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'ProductImages', N'U') IS NULL
BEGIN
    CREATE TABLE ProductImages
    (
        ProductImageId  BIGINT          IDENTITY(1,1) NOT NULL,
        ProductId       INT             NOT NULL,
        -- Relative path under wwwroot. Images are NOT stored in the database:
        -- it bloats backups and slows every restore.
        FilePath        NVARCHAR(300)   NOT NULL,
        FileName        NVARCHAR(200)   NULL,
        ContentType     NVARCHAR(100)   NULL,
        FileSizeBytes   BIGINT          NULL,
        IsPrimary       BIT             NOT NULL CONSTRAINT DF_ProductImages_IsPrimary DEFAULT (0),
        DisplayOrder    INT             NOT NULL CONSTRAINT DF_ProductImages_DisplayOrder DEFAULT (0),
        CreatedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_ProductImages_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy       INT             NULL,
        CONSTRAINT PK_ProductImages PRIMARY KEY CLUSTERED (ProductImageId),
        CONSTRAINT FK_ProductImages_Product
            FOREIGN KEY (ProductId) REFERENCES Products (ProductId) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX IX_ProductImages_ProductId
        ON ProductImages (ProductId, DisplayOrder);

    CREATE UNIQUE NONCLUSTERED INDEX UQ_ProductImages_Primary
        ON ProductImages (ProductId) WHERE IsPrimary = 1;
END
GO

/*----------------------------------------------------------------------------*/
/* ProductPriceHistory                                                     */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'ProductPriceHistory', N'U') IS NULL
BEGIN
    CREATE TABLE ProductPriceHistory
    (
        PriceHistoryId  BIGINT          IDENTITY(1,1) NOT NULL,
        ProductId       INT             NOT NULL,
        ChangedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_ProductPriceHistory_ChangedAt DEFAULT (SYSUTCDATETIME()),
        ChangedBy       INT             NULL,
        -- 'Manual' when edited on the product screen, 'Purchase' when a
        -- purchase entry pushed a new rate through.
        ChangeSource    NVARCHAR(20)    NOT NULL CONSTRAINT DF_ProductPriceHistory_Source DEFAULT ('Manual'),
        ReferenceNumber NVARCHAR(30)    NULL,

        OldPurchaseRate DECIMAL(18,4)   NULL,
        NewPurchaseRate DECIMAL(18,4)   NULL,
        OldSellingRate  DECIMAL(18,4)   NULL,
        NewSellingRate  DECIMAL(18,4)   NULL,
        OldMrp          DECIMAL(18,4)   NULL,
        NewMrp          DECIMAL(18,4)   NULL,
        Remarks         NVARCHAR(300)   NULL,

        CONSTRAINT PK_ProductPriceHistory PRIMARY KEY CLUSTERED (PriceHistoryId),
        CONSTRAINT FK_ProductPriceHistory_Product
            FOREIGN KEY (ProductId) REFERENCES Products (ProductId)
    );

    CREATE NONCLUSTERED INDEX IX_ProductPriceHistory_Product_Date
        ON ProductPriceHistory (ProductId, ChangedAt DESC);
END
GO

PRINT N'04_Products.sql completed.';
GO
