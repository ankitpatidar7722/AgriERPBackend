/*==============================================================================
  AgriERP  |  27_CustomerFatherName.sql
  ------------------------------------------------------------------------------
  Adds an optional Father's / guardian's name to the Customer master. At a
  village agri-shop several farmers share the same name; the father's name is
  the everyday way they are told apart, so it sits right after CustomerName.

  Nullable NVARCHAR(150), no constraints. Purely additive.
  Idempotent: guarded on the column existing. Safe to re-run.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 27_CustomerFatherName ---';
GO

IF COL_LENGTH(N'Customers', N'FatherName') IS NULL
BEGIN
    ALTER TABLE Customers ADD FatherName NVARCHAR(150) NULL;
    PRINT N'  added Customers.FatherName';
END
ELSE PRINT N'  Customers.FatherName already exists';
GO

/*==============================================================================
  VERIFY
==============================================================================*/
IF COL_LENGTH(N'Customers', N'FatherName') IS NOT NULL
    PRINT N'RESULT: 27_CustomerFatherName completed - Customers.FatherName in place.';
ELSE
    PRINT N'RESULT: 27_CustomerFatherName FAILED.';
GO
