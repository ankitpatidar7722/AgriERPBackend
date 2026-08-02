/*==============================================================================
  AgriERP  |  00_CreateDatabase.sql
  ------------------------------------------------------------------------------
  Creates the AgriERP database with settings suited to an OLTP ERP workload.
  Run once, from a login holding the dbcreator server role.
==============================================================================*/

USE [master];
GO

IF DB_ID(N'AgriERP') IS NULL
BEGIN
    PRINT N'Creating database [AgriERP] ...';
    CREATE DATABASE [AgriERP];
END
ELSE
BEGIN
    PRINT N'Database [AgriERP] already exists - skipping CREATE.';
END
GO

/*------------------------------------------------------------------------------
  Read Committed Snapshot Isolation.
  Long-running reports (stock ledger, GST returns) must not block billing.
  RCSI makes readers use row versions instead of taking shared locks.
------------------------------------------------------------------------------*/
ALTER DATABASE [AgriERP] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
ALTER DATABASE [AgriERP] SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

/*------------------------------------------------------------------------------
  SIMPLE recovery is fine while developing.
  BEFORE GO-LIVE: switch to FULL and schedule log backups, otherwise you can
  only restore to the last full backup and a day's billing can be lost.
------------------------------------------------------------------------------*/
ALTER DATABASE [AgriERP] SET RECOVERY SIMPLE;
GO

ALTER DATABASE [AgriERP] SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE [AgriERP] SET AUTO_UPDATE_STATISTICS ON;
ALTER DATABASE [AgriERP] SET AUTO_SHRINK OFF;      -- never auto-shrink an ERP database
ALTER DATABASE [AgriERP] SET PAGE_VERIFY CHECKSUM;
GO

PRINT N'00_CreateDatabase.sql completed.';
GO
