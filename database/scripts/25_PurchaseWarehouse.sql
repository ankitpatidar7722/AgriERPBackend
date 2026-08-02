/*==============================================================================
  AgriERP  |  25_PurchaseWarehouse.sql
  ------------------------------------------------------------------------------
  Links a GRN (Purchases) to a WarehouseMaster row. A nullable reference: it is
  the warehouse the goods were received into, shown in the UI in place of the
  storage "location". Stock still posts to the shop's StorageLocation under the
  hood - this column is the organisational label, not the stock destination.

  Idempotent: guarded on the column existing. Safe to re-run.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 25_PurchaseWarehouse ---';
GO

IF COL_LENGTH(N'Purchases', N'WarehouseId') IS NULL
BEGIN
    ALTER TABLE Purchases ADD WarehouseId INT NULL;
    PRINT N'  added Purchases.WarehouseId';
END
ELSE PRINT N'  Purchases.WarehouseId already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Purchases_Warehouse')
BEGIN
    ALTER TABLE Purchases WITH CHECK ADD CONSTRAINT FK_Purchases_Warehouse
        FOREIGN KEY (WarehouseId) REFERENCES WarehouseMaster (WarehouseId);
    PRINT N'  added FK_Purchases_Warehouse';
END
ELSE PRINT N'  FK_Purchases_Warehouse already exists';
GO

/*==============================================================================
  VERIFY
==============================================================================*/
IF COL_LENGTH(N'Purchases', N'WarehouseId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Purchases_Warehouse')
    PRINT N'RESULT: 25_PurchaseWarehouse completed - Purchases.WarehouseId + FK in place.';
ELSE
    PRINT N'RESULT: 25_PurchaseWarehouse FAILED.';
GO
