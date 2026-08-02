/*==============================================================================
  AgriERP  |  29_AccountsSupplierPayment.sql
  ------------------------------------------------------------------------------
  Consolidates money-out into the Accounts group.

  - Adds "Supplier Payment" (/accounts/supplier-payment) to the Accounts group,
    right after Customer Payment, and pushes Customer Ledger below it so the two
    payment screens sit together.
  - Removes the general "Payments" entry from Trading (/payments): its work is
    now split into Customer Payment (receipts) and Supplier Payment (payments),
    both under Accounts. Removal is a soft delete (IsDeletedTransaction = 1) - a
    hard DELETE would silently drop the row on the next demo reseed.

  Menu-only. No payment logic changes: supplier payments already post through the
  same PaymentService. Idempotent.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

PRINT N'--- 29_AccountsSupplierPayment ---';
GO

DECLARE @acctHead INT;
SELECT @acctHead = ModuleHeadDisplayOrder
FROM ModuleMaster
WHERE ModuleName = N'/accounts/customer-payment';

-- Fall back to the historical Accounts order if Customer Payment is absent.
IF @acctHead IS NULL SET @acctHead = 5;

/* 1) Supplier Payment - order 2, between Customer Payment (1) and the ledger. */
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/accounts/supplier-payment')
BEGIN
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName, IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/accounts/supplier-payment', N'Supplier Payment', N'Accounts', N'Accounts',
         @acctHead, 2, @acctHead, N'HandCoins', 0, SYSUTCDATETIME());
    PRINT N'  added /accounts/supplier-payment';
END
ELSE PRINT N'  /accounts/supplier-payment already exists';
GO

/* Keep the two payment screens together: ledger drops to 3. */
UPDATE ModuleMaster SET ModuleDisplayOrder = 3
WHERE ModuleName = N'/accounts/customer-ledger' AND ModuleDisplayOrder <> 3;
GO

/* 2) Hide the general Payments entry in Trading. */
UPDATE ModuleMaster SET IsDeletedTransaction = 1
WHERE ModuleName = N'/payments' AND IsDeletedTransaction = 0;
IF @@ROWCOUNT > 0 PRINT N'  hid Trading > /payments';
GO

/*==============================================================================
  VERIFY
==============================================================================*/
IF EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/accounts/supplier-payment' AND IsDeletedTransaction = 0)
   AND NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/payments' AND IsDeletedTransaction = 0)
    PRINT N'RESULT: 29_AccountsSupplierPayment completed - Supplier Payment under Accounts, /payments hidden.';
ELSE
    PRINT N'RESULT: 29_AccountsSupplierPayment FAILED.';
GO
