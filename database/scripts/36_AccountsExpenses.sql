/*==============================================================================
  AgriERP  |  36_AccountsExpenses.sql   (SQL Server)

  Adds "Expenses" (/accounts/expenses) to the Accounts group so shop running
  costs (rent, electricity, wages, other charges) can be recorded and deducted
  from the year's gross profit.

  Idempotent: safe to run more than once. Postgres fresh installs get this row
  from 13_seed_modules.sql; existing databases get it via a one-off INSERT.
==============================================================================*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

PRINT N'--- 36_AccountsExpenses ---';
GO

DECLARE @acctHead INT;
SELECT @acctHead = ModuleHeadDisplayOrder
FROM ModuleMaster
WHERE ModuleName = N'/accounts/customer-payment';

-- Fall back to the historical Accounts order if Customer Payment is absent.
IF @acctHead IS NULL SET @acctHead = 5;

/* Expenses - last in the Accounts group (order 5, after the two ledgers). */
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/accounts/expenses')
BEGIN
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/accounts/expenses', N'Expenses', N'Accounts', N'Accounts',
         @acctHead, 5, @acctHead, N'Receipt', 0, SYSUTCDATETIME());
    PRINT N'  added /accounts/expenses';
END
ELSE PRINT N'  /accounts/expenses already exists';
GO

IF EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/accounts/expenses' AND IsDeletedTransaction = 0)
    PRINT N'RESULT: 36_AccountsExpenses completed - Expenses under Accounts.';
ELSE
    PRINT N'RESULT: 36_AccountsExpenses FAILED.';
GO
