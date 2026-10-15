/*==============================================================================
  38_CompanyProfileDataGovKey.sql

  Adds CompanyProfile.DataGovInApiKey - the shopkeeper's free data.gov.in
  (Agmarknet) key, set from the Invoice & Licences screen so mandi prices work
  without touching server config. Read server-side only; never returned to the
  browser (the DTO exposes a boolean "key is set" instead).

  Idempotent: guarded by COL_LENGTH so re-running the 00..38 sequence is safe.
==============================================================================*/
IF COL_LENGTH('CompanyProfile', 'DataGovInApiKey') IS NULL
    ALTER TABLE CompanyProfile ADD DataGovInApiKey NVARCHAR(200) NULL;

PRINT N'38_CompanyProfileDataGovKey.sql completed.';
GO
