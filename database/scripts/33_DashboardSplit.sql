/*==============================================================================
  AgriERP  |  33_DashboardSplit.sql
  ------------------------------------------------------------------------------
  The single "Overview > Dashboard" menu item becomes a "Dashboard" group with
  two children: Sales Dashboard (/dashboard/sales) and Purchase Dashboard
  (/dashboard/purchase). Each opens only its own panel.

  The old /dashboard route is soft-deleted (IsDeletedTransaction = 1); its
  page now just redirects to /dashboard/sales, so old links keep working.

  Idempotent: safe to re-run.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 33_DashboardSplit ---';
GO

-- Retire the old single dashboard (its "Overview" head disappears with it,
-- being that head's only module).
UPDATE ModuleMaster
   SET IsDeletedTransaction = 1
 WHERE ModuleName = N'/dashboard' AND IsDeletedTransaction = 0;

-- Sales Dashboard (top of the menu, first child).
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/dashboard/sales')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/dashboard/sales', N'Sales Dashboard', N'Dashboard', N'Dashboard', 1, 1, 1, N'TrendingUp', 0, SYSUTCDATETIME());
ELSE
    UPDATE ModuleMaster
       SET IsDeletedTransaction = 0, ModuleHeadName = N'Dashboard', ModuleHeadDisplayName = N'Dashboard',
           ModuleHeadDisplayOrder = 1, ModuleDisplayOrder = 1, SetGroupIndex = 1
     WHERE ModuleName = N'/dashboard/sales';

-- Purchase Dashboard (second child).
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/dashboard/purchase')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/dashboard/purchase', N'Purchase Dashboard', N'Dashboard', N'Dashboard', 1, 2, 1, N'PieChart', 0, SYSUTCDATETIME());
ELSE
    UPDATE ModuleMaster
       SET IsDeletedTransaction = 0, ModuleHeadName = N'Dashboard', ModuleHeadDisplayName = N'Dashboard',
           ModuleHeadDisplayOrder = 1, ModuleDisplayOrder = 2, SetGroupIndex = 1
     WHERE ModuleName = N'/dashboard/purchase';
GO

PRINT N'RESULT: 33_DashboardSplit completed - Dashboard group with Sales + Purchase children.';
GO
