/*==============================================================================
  AgriERP  |  23_RequisitionModule.sql
  ------------------------------------------------------------------------------
  Adds the Purchase Requisition screen to the Inventory menu, ahead of the
  Purchase Order - the order in which the documents are raised:

      Purchase Requisition -> Purchase Order -> Purchase GRN -> Stock -> Reports

  Idempotent: matched on ModuleName, safe to re-run. Self-checks at the end.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 23_RequisitionModule ---';
GO

/* Make room: Requisition takes slot 1, everything else in Inventory shifts down. */
UPDATE ModuleMaster SET ModuleDisplayOrder = 2 WHERE ModuleName = N'/purchases/orders';
UPDATE ModuleMaster SET ModuleDisplayOrder = 3 WHERE ModuleName = N'/purchases';
UPDATE ModuleMaster SET ModuleDisplayOrder = 4 WHERE ModuleName = N'/stock';
UPDATE ModuleMaster SET ModuleDisplayOrder = 5 WHERE ModuleName = N'/reports';
GO

IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/purchases/requisitions')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex,
         IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/purchases/requisitions', N'Purchase Requisition', N'Inventory', N'Inventory',
         3, 1, 3, N'FileText', 0, SYSUTCDATETIME());
ELSE
    UPDATE ModuleMaster
    SET ModuleDisplayName = N'Purchase Requisition', ModuleHeadName = N'Inventory',
        ModuleHeadDisplayName = N'Inventory', ModuleHeadDisplayOrder = 3,
        ModuleDisplayOrder = 1, SetGroupIndex = 3, IconName = N'FileText'
    WHERE ModuleName = N'/purchases/requisitions';
GO

/*==============================================================================
  VERIFY
==============================================================================*/
DECLARE @n INT = (SELECT COUNT(*) FROM ModuleMaster
                  WHERE ModuleName = N'/purchases/requisitions'
                    AND ModuleHeadName = N'Inventory' AND ISNULL(IsDeletedTransaction, 0) = 0);
IF @n = 1
BEGIN
    PRINT N'RESULT: 23_RequisitionModule completed - Purchase Requisition added under Inventory.';
    SELECT N'  ' + ModuleHeadDisplayName + N' > ' + ModuleDisplayName + N'  ->  ' + ModuleName
    FROM ModuleMaster
    WHERE ISNULL(IsDeletedTransaction, 0) = 0
    ORDER BY ModuleHeadDisplayOrder, ModuleDisplayOrder;
END
ELSE
    PRINT N'RESULT: 23_RequisitionModule FAILED - requisition menu row not found.';
GO
