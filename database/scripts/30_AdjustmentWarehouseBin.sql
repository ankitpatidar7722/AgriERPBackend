/*==============================================================================
  AgriERP  |  30_AdjustmentWarehouseBin.sql
  ------------------------------------------------------------------------------
  Adds a Warehouse + Bin label to a physical-verification / adjustment line.

  Stock still posts to StorageLocation (LocationId) - these are the same kind of
  organisational label the GRN already carries, recording where the counted
  goods physically sit (which warehouse, which bin). Nullable, no effect on
  posting or valuation.

  Idempotent: guarded on the columns/FK existing. Safe to re-run.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 30_AdjustmentWarehouseBin ---';
GO

IF COL_LENGTH(N'StockAdjustmentDetails', N'WarehouseId') IS NULL
BEGIN
    ALTER TABLE StockAdjustmentDetails ADD WarehouseId INT NULL;
    PRINT N'  added StockAdjustmentDetails.WarehouseId';
END
ELSE PRINT N'  StockAdjustmentDetails.WarehouseId already exists';
GO

IF COL_LENGTH(N'StockAdjustmentDetails', N'BinName') IS NULL
BEGIN
    ALTER TABLE StockAdjustmentDetails ADD BinName NVARCHAR(50) NULL;
    PRINT N'  added StockAdjustmentDetails.BinName';
END
ELSE PRINT N'  StockAdjustmentDetails.BinName already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockAdjustmentDetails_Warehouse')
BEGIN
    ALTER TABLE StockAdjustmentDetails WITH CHECK ADD CONSTRAINT FK_StockAdjustmentDetails_Warehouse
        FOREIGN KEY (WarehouseId) REFERENCES WarehouseMaster (WarehouseId);
    PRINT N'  added FK_StockAdjustmentDetails_Warehouse';
END
ELSE PRINT N'  FK_StockAdjustmentDetails_Warehouse already exists';
GO

/*==============================================================================
  VERIFY
==============================================================================*/
IF COL_LENGTH(N'StockAdjustmentDetails', N'WarehouseId') IS NOT NULL
   AND COL_LENGTH(N'StockAdjustmentDetails', N'BinName') IS NOT NULL
    PRINT N'RESULT: 30_AdjustmentWarehouseBin completed.';
ELSE
    PRINT N'RESULT: 30_AdjustmentWarehouseBin FAILED.';
GO
