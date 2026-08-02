/*==============================================================================
  AgriERP  |  17_ItemModules.sql        (step 3 - the menu follows the rename)
  ------------------------------------------------------------------------------
  The sidebar is data, not code: ModuleMaster owns every entry's label,
  route, group and icon. Renaming the screens therefore means an UPDATE here,
  not an edit to nav-config.ts.

  A stale row is worse than a missing one. "Products" pointing at /products
  renders a menu item that looks perfectly normal and 404s when clicked, and
  nothing in the build or the type-check can see it - the route lives in a
  database column.

  Idempotent.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 17_ItemModules ---';
GO

UPDATE ModuleMaster
SET ModuleName = N'/items', ModuleDisplayName = N'Items'
WHERE ModuleName = N'/products';

UPDATE ModuleMaster
SET ModuleName = N'/item-subgroups', ModuleDisplayName = N'Item Sub Groups'
WHERE ModuleName = N'/categories';
GO

/*------------------------------------------------------------------------------
  Item Groups is a new screen, so it needs a row of its own. Placed directly
  above Items: a shopkeeper reads the menu top-down, and the group is the thing
  that decides what the item form asks.
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/item-groups')
BEGIN
    -- Sits in the same head as Items, one step ahead of it.
    DECLARE @head        NVARCHAR(100),
            @headDisplay NVARCHAR(100),
            @headOrder   INT,
            @order       INT,
            @setGroup    INT;

    SELECT TOP 1 @head = ModuleHeadName, @headDisplay = ModuleHeadDisplayName,
                 @headOrder = ModuleHeadDisplayOrder, @order = ModuleDisplayOrder,
                 @setGroup = SetGroupIndex
    FROM ModuleMaster WHERE ModuleName = N'/items';

    IF @head IS NOT NULL
    BEGIN
        -- Make room rather than sharing an order value with Items, which would
        -- leave the two in whatever sequence the query planner felt like.
        UPDATE ModuleMaster
        SET ModuleDisplayOrder = ModuleDisplayOrder + 1
        WHERE ModuleHeadName = @head AND ModuleDisplayOrder >= @order;

        INSERT INTO ModuleMaster
            (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
             ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex,
             IconName, IsDeletedTransaction, CreatedDate)
        VALUES
            (N'/item-groups', N'Item Groups', @head, @headDisplay,
             @headOrder, @order, ISNULL(@setGroup, 0), N'Layers', 0, SYSUTCDATETIME());
    END
END
GO

/*==============================================================================
  VERIFY
==============================================================================*/
DECLARE @problems INT = 0, @n INT;

SET @n = (SELECT COUNT(*) FROM ModuleMaster
          WHERE ModuleName IN (N'/products', N'/categories') AND ISNULL(IsDeletedTransaction, 0) = 0);
IF @n > 0 BEGIN PRINT N'FAIL: ' + CAST(@n AS NVARCHAR(10)) + N' menu row(s) still point at a renamed route'; SET @problems += 1; END

SET @n = (SELECT COUNT(*) FROM ModuleMaster
          WHERE ModuleName IN (N'/items', N'/item-subgroups', N'/item-groups') AND ISNULL(IsDeletedTransaction, 0) = 0);
IF @n <> 3 BEGIN PRINT N'FAIL: expected 3 item menu rows, found ' + CAST(@n AS NVARCHAR(10)); SET @problems += 1; END

IF @problems = 0
BEGIN
    PRINT N'RESULT: 17_ItemModules completed - all checks passed.';
    SELECT N'  ' + ModuleHeadDisplayName + N' > ' + ModuleDisplayName + N'  ->  ' + ModuleName
    FROM ModuleMaster
    WHERE ISNULL(IsDeletedTransaction, 0) = 0
    ORDER BY ModuleHeadDisplayOrder, ModuleDisplayOrder;
END
ELSE
    PRINT N'RESULT: 17_ItemModules finished with ' + CAST(@problems AS NVARCHAR(10)) + N' problem(s).';
GO
