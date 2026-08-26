/*==============================================================================
  35_BulkImportModule.sql

  Adds the "Bulk Import" entry to the DB-driven sidebar (ModuleMaster), under the
  existing "Masters" head. The screen (/bulk-import) lets users import Customers,
  Suppliers and Items from Excel into the same tables the manual create writes to.

  Idempotent: guarded by ModuleName so re-running the 00..35 sequence is safe.
  Left out of ModuleService.RoutePermissions on purpose, so the link is visible
  to every signed-in user; the import endpoints stay gated by their own
  [HasPermission(Customer/Supplier/Item .Create)] attributes.
==============================================================================*/
IF NOT EXISTS (SELECT 1 FROM ModuleMaster WHERE ModuleName = N'/bulk-import')
    INSERT INTO ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, IconName,
         IsDeletedTransaction, CreatedDate)
    VALUES
        (N'/bulk-import', N'Bulk Import', N'Masters', N'Masters',
         4, 20, 4, N'Upload',
         0, SYSUTCDATETIME());

PRINT N'35_BulkImportModule.sql completed.';
GO
