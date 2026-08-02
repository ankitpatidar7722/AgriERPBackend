/*==============================================================================
  AgriERP  |  14_ItemRestructure.sql        (step 1a - renames only)
  ------------------------------------------------------------------------------
  Products become Items, Categories become Item Sub Groups.

  WHY A MIGRATION RATHER THAN AN EDIT TO 04_Products.sql
  ------------------------------------------------------
  The database already holds live rows - items, batches, invoices, stock journal.
  Editing the CREATE scripts would only help a brand-new database and would
  silently do nothing here, so the rename has to be expressed as a transform of
  what exists. Scripts 00-13 still build the OLD shape; this one moves it to the
  new one. Run them in order and a fresh install lands in exactly the same place
  as this database does.

  WHY sp_rename AND NOT "CREATE new + INSERT + DROP old"
  -----------------------------------------------------
  sp_rename keeps the object identity: every foreign key, index, check
  constraint, computed column and identity seed survives untouched, and no row
  is copied. Rebuilding the tables instead would mean re-pointing 11 foreign
  keys by hand and re-seeding IDENTITY on a table that invoices already
  reference - a far larger surface for a silent mistake.

  This script is IDEMPOTENT: each rename is guarded, so re-running it is a
  no-op rather than an error.

  WHAT IT DOES NOT DO
  -------------------
  It does not create ItemGroupMaster / ItemGroupFieldMaster, and it does not
  touch views or procedures. Those are 15_ItemGroups.sql and a re-run of
  10_Views.sql / 11_StoredProcedures.sql, which use CREATE OR ALTER and so pick
  up the new names on the next execution. Splitting them keeps each step small
  enough to verify on its own.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

SET NOCOUNT ON;
GO

PRINT N'--- 14_ItemRestructure: renaming Products -> Items ---';
GO

/*==============================================================================
  1. TABLES
==============================================================================*/
IF OBJECT_ID(N'Categories', N'U') IS NOT NULL AND OBJECT_ID(N'ItemSubGroupMaster', N'U') IS NULL
    EXEC sp_rename N'Categories', N'ItemSubGroupMaster';
GO
IF OBJECT_ID(N'Products', N'U') IS NOT NULL AND OBJECT_ID(N'ItemMaster', N'U') IS NULL
    EXEC sp_rename N'Products', N'ItemMaster';
GO
IF OBJECT_ID(N'ProductBatches', N'U') IS NOT NULL AND OBJECT_ID(N'ItemBatches', N'U') IS NULL
    EXEC sp_rename N'ProductBatches', N'ItemBatches';
GO
IF OBJECT_ID(N'ProductImages', N'U') IS NOT NULL AND OBJECT_ID(N'ItemImages', N'U') IS NULL
    EXEC sp_rename N'ProductImages', N'ItemImages';
GO
IF OBJECT_ID(N'ProductPriceHistory', N'U') IS NOT NULL AND OBJECT_ID(N'ItemPriceHistory', N'U') IS NULL
    EXEC sp_rename N'ProductPriceHistory', N'ItemPriceHistory';
GO

/*==============================================================================
  2. COLUMNS

  A column rename rebinds every index, CHECK constraint and persisted computed
  column that uses it - those reference the column by id, not by text - so
  nothing has to be dropped and recreated.
==============================================================================*/
/*--------------------------- ItemSubGroupMaster -------------------------*/
IF COL_LENGTH(N'ItemSubGroupMaster', N'CategoryId') IS NOT NULL
    EXEC sp_rename N'ItemSubGroupMaster.CategoryId', N'ItemSubGroupId', N'COLUMN';
GO
IF COL_LENGTH(N'ItemSubGroupMaster', N'CategoryCode') IS NOT NULL
    EXEC sp_rename N'ItemSubGroupMaster.CategoryCode', N'ItemSubGroupCode', N'COLUMN';
GO
IF COL_LENGTH(N'ItemSubGroupMaster', N'CategoryName') IS NOT NULL
    EXEC sp_rename N'ItemSubGroupMaster.CategoryName', N'ItemSubGroupName', N'COLUMN';
GO
IF COL_LENGTH(N'ItemSubGroupMaster', N'ParentCategoryId') IS NOT NULL
    EXEC sp_rename N'ItemSubGroupMaster.ParentCategoryId', N'ParentItemSubGroupId', N'COLUMN';
GO

/*-------------------------------- ItemMaster ----------------------------*/
IF COL_LENGTH(N'ItemMaster', N'ProductId') IS NOT NULL
    EXEC sp_rename N'ItemMaster.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'ItemMaster', N'ProductCode') IS NOT NULL
    EXEC sp_rename N'ItemMaster.ProductCode', N'ItemCode', N'COLUMN';
GO
IF COL_LENGTH(N'ItemMaster', N'ProductName') IS NOT NULL
    EXEC sp_rename N'ItemMaster.ProductName', N'ItemName', N'COLUMN';
GO
IF COL_LENGTH(N'ItemMaster', N'CategoryId') IS NOT NULL
    EXEC sp_rename N'ItemMaster.CategoryId', N'ItemSubGroupId', N'COLUMN';
GO

/*----------------------- child tables of the item master --------------------*/
IF COL_LENGTH(N'ItemBatches', N'ProductId') IS NOT NULL
    EXEC sp_rename N'ItemBatches.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'ItemImages', N'ProductImageId') IS NOT NULL
    EXEC sp_rename N'ItemImages.ProductImageId', N'ItemImageId', N'COLUMN';
GO
IF COL_LENGTH(N'ItemImages', N'ProductId') IS NOT NULL
    EXEC sp_rename N'ItemImages.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'ItemPriceHistory', N'ProductId') IS NOT NULL
    EXEC sp_rename N'ItemPriceHistory.ProductId', N'ItemId', N'COLUMN';
GO

/*----------------- the 8 transaction tables that carry ProductId ------------*/
IF COL_LENGTH(N'StockTransactions', N'ProductId') IS NOT NULL
    EXEC sp_rename N'StockTransactions.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'StockAdjustmentDetails', N'ProductId') IS NOT NULL
    EXEC sp_rename N'StockAdjustmentDetails.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'StockTransferDetails', N'ProductId') IS NOT NULL
    EXEC sp_rename N'StockTransferDetails.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'PurchaseDetails', N'ProductId') IS NOT NULL
    EXEC sp_rename N'PurchaseDetails.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'PurchaseOrderDetails', N'ProductId') IS NOT NULL
    EXEC sp_rename N'PurchaseOrderDetails.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'PurchaseReturnDetails', N'ProductId') IS NOT NULL
    EXEC sp_rename N'PurchaseReturnDetails.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'SalesDetails', N'ProductId') IS NOT NULL
    EXEC sp_rename N'SalesDetails.ProductId', N'ItemId', N'COLUMN';
GO
IF COL_LENGTH(N'SalesReturnDetails', N'ProductId') IS NOT NULL
    EXEC sp_rename N'SalesReturnDetails.ProductId', N'ItemId', N'COLUMN';
GO

/*==============================================================================
  3. CONSTRAINT AND INDEX NAMES

  Renaming the tables leaves PK_Products, FK_ProductBatches_Product,
  UQ_Products_ProductCode and friends behind. Nothing breaks - names are labels
  - but a schema that half-says "Product" is a trap for whoever reads it next,
  and the persistence tests assert on some of them.

  Driven from the catalog rather than written out: there are roughly seventy,
  the transformation is purely textual, and a hand-written list would drift the
  first time an index is added. The scope is deliberately narrow - only objects
  that belong to the tables renamed above.
==============================================================================*/
DECLARE @renames TABLE (OldName SYSNAME, NewName SYSNAME, Kind NVARCHAR(10), Parent SYSNAME);

-- Constraints (PK, FK, CHECK, DEFAULT) live in sys.objects and are renamed as
-- OBJECT; indexes need the 'schema.table.index' form and kind INDEX.
INSERT INTO @renames (OldName, NewName, Kind, Parent)
SELECT o.name,
       REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(o.name,
           'ProductPriceHistory', 'ItemPriceHistory'),
           'ProductBatches',      'ItemBatches'),
           'ProductImages',       'ItemImages'),
           'Products',            'ItemMaster'),
           'Categories',          'ItemSubGroupMaster'),
       'OBJECT',
       SCHEMA_NAME(t.schema_id) + '.' + t.name
FROM sys.objects o
JOIN sys.tables  t ON t.object_id = o.parent_object_id
WHERE o.type IN ('PK', 'F', 'C', 'D', 'UQ')
  AND t.name IN ('ItemMaster', 'ItemSubGroupMaster', 'ItemBatches', 'ItemImages', 'ItemPriceHistory')
  AND (o.name LIKE '%Product%' OR o.name LIKE '%Categor%');

INSERT INTO @renames (OldName, NewName, Kind, Parent)
SELECT i.name,
       REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(i.name,
           'ProductPriceHistory', 'ItemPriceHistory'),
           'ProductBatches',      'ItemBatches'),
           'ProductImages',       'ItemImages'),
           'Products',            'ItemMaster'),
           'Categories',          'ItemSubGroupMaster'),
       'INDEX',
       SCHEMA_NAME(t.schema_id) + '.' + t.name
FROM sys.indexes i
JOIN sys.tables  t ON t.object_id = i.object_id
WHERE i.name IS NOT NULL
  AND t.name IN ('ItemMaster', 'ItemSubGroupMaster', 'ItemBatches', 'ItemImages', 'ItemPriceHistory')
  AND (i.name LIKE '%Product%' OR i.name LIKE '%Categor%')
  AND i.is_primary_key = 0 AND i.is_unique_constraint = 0;   -- those came through as objects

-- Second pass on the column half of the name: UQ_ItemMaster_ProductCode still
-- carries the old COLUMN name after the table part was fixed above.
UPDATE @renames
SET NewName = REPLACE(REPLACE(REPLACE(REPLACE(NewName,
    'ProductCode', 'ItemCode'),
    'ProductName', 'ItemName'),
    'ProductId',   'ItemId'),
    'CategoryId',  'ItemSubGroupId');

UPDATE @renames SET NewName = REPLACE(NewName, '_Product', '_Item');
UPDATE @renames SET NewName = REPLACE(NewName, '_Category', '_ItemSubGroup');

DECLARE @old SYSNAME, @new SYSNAME, @kind NVARCHAR(10), @parent SYSNAME, @n INT = 0;
DECLARE renamer CURSOR LOCAL FAST_FORWARD FOR
    SELECT OldName, NewName, Kind, Parent FROM @renames WHERE OldName <> NewName;
OPEN renamer;
FETCH NEXT FROM renamer INTO @old, @new, @kind, @parent;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF @kind = 'OBJECT'
    BEGIN
        -- The schema prefix is REQUIRED here. A bare constraint name makes
        -- sp_rename reply "Either the parameter @objname is ambiguous or the
        -- claimed @objtype (OBJECT) is wrong", which reads like a type problem
        -- and is really a missing qualifier.
        DECLARE @schema SYSNAME = PARSENAME(@parent, 2);
        DECLARE @qualifiedOld NVARCHAR(400) = @schema + '.' + @old;

        IF OBJECT_ID(@qualifiedOld) IS NOT NULL
           AND OBJECT_ID(@schema + '.' + @new) IS NULL
        BEGIN
            EXEC sp_rename @objname = @qualifiedOld, @newname = @new, @objtype = 'OBJECT';
            SET @n += 1;
        END
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(@parent) AND name = @old)
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(@parent) AND name = @new)
        BEGIN
            DECLARE @target NVARCHAR(400) = @parent + '.' + @old;
            EXEC sp_rename @objname = @target, @newname = @new, @objtype = 'INDEX';
            SET @n += 1;
        END
    END
    FETCH NEXT FROM renamer INTO @old, @new, @kind, @parent;
END
CLOSE renamer;
DEALLOCATE renamer;

PRINT N'Renamed ' + CAST(@n AS NVARCHAR(10)) + N' constraints/indexes.';
GO

/*==============================================================================
  4. VERIFY
==============================================================================*/
DECLARE @problems INT = 0;

IF OBJECT_ID(N'ItemMaster', N'U') IS NULL
    BEGIN PRINT N'FAIL: ItemMaster does not exist.'; SET @problems += 1; END
IF OBJECT_ID(N'ItemSubGroupMaster', N'U') IS NULL
    BEGIN PRINT N'FAIL: ItemSubGroupMaster does not exist.'; SET @problems += 1; END
IF OBJECT_ID(N'Products', N'U') IS NOT NULL
    BEGIN PRINT N'FAIL: Products still exists.'; SET @problems += 1; END

-- Every ProductId is gone from every TABLE.
--
-- Tables only, deliberately: the views still project ProductId / CategoryId
-- because their definitions are stored text that a rename cannot reach. They
-- are rebuilt by re-running 10_Views.sql, which is the next step, so counting
-- them here would report a failure for something that is expected.
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
           WHERE c.name IN ('ProductId', 'CategoryId'))
BEGIN
    PRINT N'FAIL: a ProductId/CategoryId column survives on a table:';
    SELECT N'   ' + OBJECT_SCHEMA_NAME(c.object_id) + N'.' + OBJECT_NAME(c.object_id) + N'.' + c.name
    FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
    WHERE c.name IN ('ProductId', 'CategoryId');
    SET @problems += 1;
END

-- No constraint or index may still say "Product" on the renamed tables.
IF EXISTS (SELECT 1 FROM sys.objects o JOIN sys.tables t ON t.object_id = o.parent_object_id
           WHERE t.name IN ('ItemMaster','ItemSubGroupMaster','ItemBatches','ItemImages','ItemPriceHistory')
             AND (o.name LIKE '%Product%' OR o.name LIKE '%Categor%'))
BEGIN
    PRINT N'FAIL: a constraint still carries the old name:';
    SELECT N'   ' + o.name FROM sys.objects o JOIN sys.tables t ON t.object_id = o.parent_object_id
    WHERE t.name IN ('ItemMaster','ItemSubGroupMaster','ItemBatches','ItemImages','ItemPriceHistory')
      AND (o.name LIKE '%Product%' OR o.name LIKE '%Categor%');
    SET @problems += 1;
END

-- The 11 foreign keys must still point at the renamed master.
DECLARE @fks INT = (SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('ItemMaster'));
IF @fks <> 11
    BEGIN PRINT N'FAIL: expected 11 FKs into ItemMaster, found ' + CAST(@fks AS NVARCHAR(10)); SET @problems += 1; END

IF @problems = 0
    PRINT N'RESULT: 14_ItemRestructure completed - all checks passed.';
ELSE
    PRINT N'RESULT: 14_ItemRestructure finished with ' + CAST(@problems AS NVARCHAR(10)) + N' problem(s).';
GO
