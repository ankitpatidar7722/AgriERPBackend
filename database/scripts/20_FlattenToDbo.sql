/*==============================================================================
  AgriERP  |  20_FlattenToDbo.sql        (migration for existing databases)
  ------------------------------------------------------------------------------
  Moves every object out of the seven logical schemas (sec, mst, inv, pur, sal,
  fin, app) into the default [dbo], so a table is addressed by its plain name -
  ItemMaster, not mst.ItemMaster.

  A fresh install never needs this: scripts 00->19 already build straight into
  dbo. This script is only for a database created before the flatten. It is
  idempotent - once everything is in dbo and the schemas are gone, re-running
  finds nothing to move.

  ORDER MATTERS
    1. Drop the views and procedures. Their bodies still say mst.X / inv.X; once
       the tables move they would be broken definitions. 10_Views.sql and
       11_StoredProcedures.sql recreate them in dbo straight after this runs.
    2. ALTER SCHEMA TRANSFER each table into dbo. Data, keys, indexes, checks and
       computed columns all travel with the table.
    3. Drop the now-empty schemas.

  Run this, THEN re-run 10_Views.sql and 11_StoredProcedures.sql.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 20_FlattenToDbo ---';
GO

/*----------------------------------------------------------------------------*/
/* 1. Drop views + procedures living in the custom schemas                     */
/*----------------------------------------------------------------------------*/
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'DROP VIEW ' + QUOTENAME(s.name) + N'.' + QUOTENAME(v.name) + N';' + CHAR(10)
FROM sys.views v JOIN sys.schemas s ON s.schema_id = v.schema_id
WHERE s.name IN (N'sec', N'mst', N'inv', N'pur', N'sal', N'fin', N'app');
SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';' + CHAR(10)
FROM sys.procedures p JOIN sys.schemas s ON s.schema_id = p.schema_id
WHERE s.name IN (N'sec', N'mst', N'inv', N'pur', N'sal', N'fin', N'app');
IF LEN(@sql) > 0 EXEC sys.sp_executesql @sql;
GO

/*----------------------------------------------------------------------------*/
/* 2. Transfer every table into dbo                                            */
/*----------------------------------------------------------------------------*/
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'ALTER SCHEMA dbo TRANSFER ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN (N'sec', N'mst', N'inv', N'pur', N'sal', N'fin', N'app');
IF LEN(@sql) > 0 EXEC sys.sp_executesql @sql;
GO

/*----------------------------------------------------------------------------*/
/* 3. Drop the now-empty schemas                                               */
/*----------------------------------------------------------------------------*/
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'DROP SCHEMA ' + QUOTENAME(name) + N';' + CHAR(10)
FROM sys.schemas
WHERE name IN (N'sec', N'mst', N'inv', N'pur', N'sal', N'fin', N'app');
IF LEN(@sql) > 0 EXEC sys.sp_executesql @sql;
GO

/*----------------------------------------------------------------------------*/
/* self-check                                                                  */
/*----------------------------------------------------------------------------*/
DECLARE @schemasLeft INT =
    (SELECT COUNT(*) FROM sys.schemas WHERE name IN (N'sec', N'mst', N'inv', N'pur', N'sal', N'fin', N'app'));
DECLARE @nonDbo INT =
    (SELECT COUNT(*) FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
     WHERE o.type IN ('U', 'V', 'P') AND s.name <> N'dbo'
       AND s.name NOT IN (N'sys', N'INFORMATION_SCHEMA'));

IF @schemasLeft = 0 AND @nonDbo = 0
    PRINT N'20_FlattenToDbo.sql completed. Every table/view/proc lives in dbo; custom schemas dropped. Now re-run 10_Views.sql and 11_StoredProcedures.sql.';
ELSE
    PRINT N'20_FlattenToDbo WARNING: schemasLeft=' + CAST(@schemasLeft AS NVARCHAR(10))
        + N', nonDboObjects=' + CAST(@nonDbo AS NVARCHAR(10));
GO
