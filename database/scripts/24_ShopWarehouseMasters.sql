/*==============================================================================
  AgriERP  |  24_ShopWarehouseMasters.sql
  ------------------------------------------------------------------------------
  Two new standalone masters, surfaced on ONE screen with two tabs
  ("Shop & Warehouse" under the Masters menu group):

    * ShopMaster      - a shop/outlet's details (name, address, owner, GST...).
                        Deliberately separate from CompanyProfile (the single
                        invoice-header identity) - this is an editable list.
    * WarehouseMaster - a warehouse with an auto-generated code (WH00001...),
                        a name, an address, and a list of bins (WarehouseBins).
                        Separate from StorageLocations (which drive stock
                        posting) - nothing here touches purchase/stock/batches.

  Idempotent: guarded by IF OBJECT_ID / IF NOT EXISTS, safe to re-run. Verifies
  at the end.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 24_ShopWarehouseMasters ---';
GO

/*----------------------------------------------------------------------------*/
/* ShopMaster                                                                 */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'ShopMaster', N'U') IS NULL
BEGIN
    CREATE TABLE ShopMaster
    (
        ShopId      INT             IDENTITY(1,1) NOT NULL,
        ShopName    NVARCHAR(150)   NOT NULL,
        Address     NVARCHAR(500)   NULL,
        City        NVARCHAR(80)    NULL,
        StateId     INT             NULL,
        GstNo       NVARCHAR(15)    NULL,
        OwnerName   NVARCHAR(120)   NULL,
        MobileNo    NVARCHAR(15)    NULL,
        Email       NVARCHAR(150)   NULL,
        IsActive    BIT             NOT NULL CONSTRAINT DF_ShopMaster_IsActive  DEFAULT (1),
        IsDeleted   BIT             NOT NULL CONSTRAINT DF_ShopMaster_IsDeleted DEFAULT (0),
        CreatedAt   DATETIME2(3)    NOT NULL CONSTRAINT DF_ShopMaster_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy   INT             NULL,
        UpdatedAt   DATETIME2(3)    NULL,
        UpdatedBy   INT             NULL,
        RowVersion  ROWVERSION      NOT NULL,
        CONSTRAINT PK_ShopMaster PRIMARY KEY CLUSTERED (ShopId),
        CONSTRAINT FK_ShopMaster_State FOREIGN KEY (StateId) REFERENCES States (StateId)
    );

    -- One live shop per name; a re-added (soft-deleted) name is allowed back.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_ShopMaster_Name
        ON ShopMaster (ShopName) WHERE IsDeleted = 0;

    PRINT N'  created ShopMaster';
END
ELSE PRINT N'  ShopMaster already exists';
GO

/*----------------------------------------------------------------------------*/
/* WarehouseMaster + WarehouseBins                                            */
/*----------------------------------------------------------------------------*/
IF OBJECT_ID(N'WarehouseMaster', N'U') IS NULL
BEGIN
    CREATE TABLE WarehouseMaster
    (
        WarehouseId   INT             IDENTITY(1,1) NOT NULL,
        WarehouseCode NVARCHAR(20)    NOT NULL,
        WarehouseName NVARCHAR(150)   NOT NULL,
        Address       NVARCHAR(500)   NULL,
        IsActive      BIT             NOT NULL CONSTRAINT DF_WarehouseMaster_IsActive  DEFAULT (1),
        IsDeleted     BIT             NOT NULL CONSTRAINT DF_WarehouseMaster_IsDeleted DEFAULT (0),
        CreatedAt     DATETIME2(3)    NOT NULL CONSTRAINT DF_WarehouseMaster_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy     INT             NULL,
        UpdatedAt     DATETIME2(3)    NULL,
        UpdatedBy     INT             NULL,
        RowVersion    ROWVERSION      NOT NULL,
        CONSTRAINT PK_WarehouseMaster PRIMARY KEY CLUSTERED (WarehouseId)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_WarehouseMaster_Code
        ON WarehouseMaster (WarehouseCode) WHERE IsDeleted = 0;

    PRINT N'  created WarehouseMaster';
END
ELSE PRINT N'  WarehouseMaster already exists';
GO

IF OBJECT_ID(N'WarehouseBins', N'U') IS NULL
BEGIN
    CREATE TABLE WarehouseBins
    (
        WarehouseBinId INT            IDENTITY(1,1) NOT NULL,
        WarehouseId    INT            NOT NULL,
        BinName        NVARCHAR(100)  NOT NULL,
        CONSTRAINT PK_WarehouseBins PRIMARY KEY CLUSTERED (WarehouseBinId),
        -- Bins belong to their warehouse: replaced wholesale on edit, and the
        -- cascade cleans them if a warehouse is ever hard-removed.
        CONSTRAINT FK_WarehouseBins_Warehouse
            FOREIGN KEY (WarehouseId) REFERENCES WarehouseMaster (WarehouseId) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX IX_WarehouseBins_WarehouseId
        ON WarehouseBins (WarehouseId);

    PRINT N'  created WarehouseBins';
END
ELSE PRINT N'  WarehouseBins already exists';
GO

/*----------------------------------------------------------------------------*/
/* NumberSeries: Warehouse code WH00001 (no year, no separator, like a master)*/
/*----------------------------------------------------------------------------*/
DECLARE @ActiveFy INT = (SELECT FinancialYearId FROM FinancialYears WHERE IsActive = 1);

IF NOT EXISTS (SELECT 1 FROM NumberSeries WHERE DocumentType = N'Warehouse' AND FinancialYearId = @ActiveFy)
    INSERT INTO NumberSeries (DocumentType, FinancialYearId, Prefix, Suffix, Separator, IncludeYearCode, PaddingLength)
    VALUES (N'Warehouse', @ActiveFy, N'WH', N'', N'', 0, 5);   -- WH + 00001
GO

/*----------------------------------------------------------------------------*/
/* Menu: one row under the Masters group (page has both tabs)                 */
/*----------------------------------------------------------------------------*/
DECLARE @headOrder INT, @setIdx INT, @nextOrder INT;
SELECT @headOrder = ISNULL(MAX(ModuleHeadDisplayOrder), 4),
       @setIdx    = ISNULL(MAX(SetGroupIndex), 4),
       @nextOrder = ISNULL(MAX(ModuleDisplayOrder), 0) + 1
FROM ModuleMaster
WHERE ModuleHeadName = N'Masters' AND ISNULL(IsDeletedTransaction, 0) = 0;

IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/shop-warehouse')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex,
         IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/shop-warehouse', N'Shop & Warehouse', N'Masters', N'Masters',
         @headOrder, @nextOrder, @setIdx, N'Store', 0, SYSUTCDATETIME());
ELSE
    UPDATE ModuleMaster
    SET ModuleDisplayName = N'Shop & Warehouse', ModuleHeadName = N'Masters',
        ModuleHeadDisplayName = N'Masters', IconName = N'Store', IsDeletedTransaction = 0
    WHERE ModuleName = N'/shop-warehouse';
GO

/*==============================================================================
  VERIFY
==============================================================================*/
DECLARE @ok BIT = 1;
IF OBJECT_ID(N'ShopMaster', N'U')      IS NULL SET @ok = 0;
IF OBJECT_ID(N'WarehouseMaster', N'U') IS NULL SET @ok = 0;
IF OBJECT_ID(N'WarehouseBins', N'U')   IS NULL SET @ok = 0;
IF NOT EXISTS (SELECT 1 FROM NumberSeries WHERE DocumentType = N'Warehouse') SET @ok = 0;
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/shop-warehouse') SET @ok = 0;

IF @ok = 1
    PRINT N'RESULT: 24_ShopWarehouseMasters completed - ShopMaster, WarehouseMaster, WarehouseBins, WH number series and menu are in place.';
ELSE
    PRINT N'RESULT: 24_ShopWarehouseMasters FAILED - one or more objects missing.';
GO
