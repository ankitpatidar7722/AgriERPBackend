/*==============================================================================
  AgriERP  |  37_SalesReturnModule.sql   (SQL Server)

  Adds "Sales Return" (/sales/returns) to the Trading group so customer returns
  (credit notes) get a sidebar entry. The whole Sales Return backend already
  exists; this only surfaces the screen in the menu.

  Idempotent. Postgres fresh installs get this row from 13_seed_modules.sql;
  existing databases get it via a one-off INSERT.
==============================================================================*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

PRINT N'--- 37_SalesReturnModule ---';
GO

DECLARE @tradeHead INT;
SELECT @tradeHead = ModuleHeadDisplayOrder FROM ModuleMaster WHERE ModuleName = N'/sales';
IF @tradeHead IS NULL SET @tradeHead = 2;

/* Sales Return - right after Sales in the Trading group. */
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/sales/returns')
BEGIN
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/sales/returns', N'Sales Return', N'Trading', N'Trading',
         @tradeHead, 2, @tradeHead, N'Undo2', 0, SYSUTCDATETIME());
    PRINT N'  added /sales/returns';
END
ELSE PRINT N'  /sales/returns already exists';
GO

IF EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/sales/returns' AND IsDeletedTransaction = 0)
    PRINT N'RESULT: 37_SalesReturnModule completed - Sales Return under Trading.';
ELSE
    PRINT N'RESULT: 37_SalesReturnModule FAILED.';
GO
