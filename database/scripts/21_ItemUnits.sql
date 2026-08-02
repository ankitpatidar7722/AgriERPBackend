/*==============================================================================
  AgriERP  |  21_ItemUnits.sql
  ------------------------------------------------------------------------------
  An item now records three units, not one:
    UnitId          - the SELLING unit (already present; "Selling unit" on the form)
    PurchaseUnitId   - the unit it is BOUGHT in
    StockUnitId      - the unit it is STOCKED / counted in

  Both new columns are nullable - existing items simply have none, and the
  selling unit is used as the fallback. They apply to every item group, since
  they sit on the shared ItemMaster, not on a group's field list.

  Idempotent: safe to re-run. Self-checks at the end.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('ItemMaster', 'PurchaseUnitId') IS NULL
    ALTER TABLE ItemMaster ADD PurchaseUnitId INT NULL;
GO
IF COL_LENGTH('ItemMaster', 'StockUnitId') IS NULL
    ALTER TABLE ItemMaster ADD StockUnitId INT NULL;
GO

IF OBJECT_ID(N'FK_ItemMaster_PurchaseUnit', N'F') IS NULL
    ALTER TABLE ItemMaster ADD CONSTRAINT FK_ItemMaster_PurchaseUnit
        FOREIGN KEY (PurchaseUnitId) REFERENCES Units (UnitId);
GO
IF OBJECT_ID(N'FK_ItemMaster_StockUnit', N'F') IS NULL
    ALTER TABLE ItemMaster ADD CONSTRAINT FK_ItemMaster_StockUnit
        FOREIGN KEY (StockUnitId) REFERENCES Units (UnitId);
GO

/*----------------------------------------------------------------------------*/
/* self-check                                                                  */
/*----------------------------------------------------------------------------*/
DECLARE @cols INT =
    (SELECT COUNT(*) FROM sys.columns
     WHERE object_id = OBJECT_ID('ItemMaster') AND name IN ('PurchaseUnitId', 'StockUnitId'));
DECLARE @fks INT =
    (SELECT COUNT(*) FROM sys.foreign_keys
     WHERE name IN ('FK_ItemMaster_PurchaseUnit', 'FK_ItemMaster_StockUnit'));

IF @cols = 2 AND @fks = 2
    PRINT N'21_ItemUnits.sql completed. PurchaseUnitId + StockUnitId added with FKs to Units.';
ELSE
    PRINT N'21_ItemUnits.sql WARNING: columns=' + CAST(@cols AS NVARCHAR(10))
        + N', fks=' + CAST(@fks AS NVARCHAR(10));
GO
