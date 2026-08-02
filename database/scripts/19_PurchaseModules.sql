/*==============================================================================
  AgriERP  |  19_PurchaseModules.sql
  ------------------------------------------------------------------------------
  The menu follows the purchase split into two documents.

  A purchase is now two steps, each its own screen:
    - Purchase Order - the booking (PurchaseOrders, voucher PO)
    - Purchase GRN   - goods received into stock (Purchases, voucher PGRN)

  Both belong under Inventory. The old single "Purchases" entry (which lived
  under Trading and pointed at /purchases) IS the goods-receipt register, so it
  is relabelled "Purchase GRN" and moved into Inventory rather than duplicated.
  A brand-new "Purchase Order" entry points at /purchases/orders.

  Inventory order after this runs: Purchase Order, Purchase GRN, Stock, Reports.

  Idempotent: matched on ModuleName, safe to re-run. Self-checks at the end.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 19_PurchaseModules ---';
GO

/*----------------------------------------------------------------------------*/
/* 1. Relabel + move the existing /purchases entry -> Inventory > Purchase GRN */
/*----------------------------------------------------------------------------*/
UPDATE ModuleMaster
SET ModuleDisplayName      = N'Purchase GRN',
    ModuleHeadName         = N'Inventory',
    ModuleHeadDisplayName  = N'Inventory',
    ModuleHeadDisplayOrder = 3,
    ModuleDisplayOrder     = 2,
    SetGroupIndex          = 3,
    IconName               = N'PackageCheck'
WHERE ModuleName = N'/purchases';
GO

/*----------------------------------------------------------------------------*/
/* 2. Keep Stock and Reports below the two purchase steps                      */
/*----------------------------------------------------------------------------*/
UPDATE ModuleMaster SET ModuleDisplayOrder = 3 WHERE ModuleName = N'/stock';
UPDATE ModuleMaster SET ModuleDisplayOrder = 4 WHERE ModuleName = N'/reports';
GO

/*----------------------------------------------------------------------------*/
/* 3. New entry: Inventory > Purchase Order -> /purchases/orders               */
/*----------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/purchases/orders')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex,
         IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/purchases/orders', N'Purchase Order', N'Inventory', N'Inventory',
         3, 1, 3, N'ClipboardList', 0, SYSUTCDATETIME());
ELSE
    UPDATE ModuleMaster
    SET ModuleDisplayName = N'Purchase Order', ModuleHeadName = N'Inventory',
        ModuleHeadDisplayName = N'Inventory', ModuleHeadDisplayOrder = 3,
        ModuleDisplayOrder = 1, SetGroupIndex = 3, IconName = N'ClipboardList'
    WHERE ModuleName = N'/purchases/orders';
GO

/*==============================================================================
  VERIFY
==============================================================================*/
DECLARE @problems INT = 0, @n INT;

-- Both purchase steps live, both under Inventory.
SET @n = (SELECT COUNT(*) FROM ModuleMaster
          WHERE ModuleName IN (N'/purchases', N'/purchases/orders')
            AND ModuleHeadName = N'Inventory' AND ISNULL(IsDeletedTransaction, 0) = 0);
IF @n <> 2 BEGIN PRINT N'FAIL: expected 2 Inventory purchase rows, found ' + CAST(@n AS NVARCHAR(10)); SET @problems += 1; END

-- Nothing purchase-related left stranded under Trading.
SET @n = (SELECT COUNT(*) FROM ModuleMaster
          WHERE ModuleName = N'/purchases' AND ModuleHeadName = N'Trading' AND ISNULL(IsDeletedTransaction, 0) = 0);
IF @n <> 0 BEGIN PRINT N'FAIL: /purchases still under Trading'; SET @problems += 1; END

IF @problems = 0
BEGIN
    PRINT N'RESULT: 19_PurchaseModules completed - all checks passed.';
    SELECT N'  ' + ModuleHeadDisplayName + N' > ' + ModuleDisplayName + N'  ->  ' + ModuleName
    FROM ModuleMaster
    WHERE ISNULL(IsDeletedTransaction, 0) = 0
    ORDER BY ModuleHeadDisplayOrder, ModuleDisplayOrder;
END
ELSE
    PRINT N'RESULT: 19_PurchaseModules finished with ' + CAST(@problems AS NVARCHAR(10)) + N' problem(s).';
GO
