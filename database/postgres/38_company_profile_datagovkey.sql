-- 38_company_profile_datagovkey.sql
-- Adds CompanyProfile.DataGovInApiKey to an EXISTING PostgreSQL database (e.g. Neon).
-- Fresh builds already get the column from 09_system.sql; this idempotent ALTER
-- brings an already-created database up to date. Safe to run repeatedly.
ALTER TABLE "CompanyProfile" ADD COLUMN IF NOT EXISTS "DataGovInApiKey" varchar(200);
