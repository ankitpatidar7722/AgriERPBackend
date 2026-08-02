/*==============================================================================
  AgriERP  |  01_Schemas.sql
  ------------------------------------------------------------------------------
  Every table, view and procedure lives in the default [dbo] schema, so an
  object is addressed by its plain name - ItemMaster, not mst.ItemMaster.

  This script used to create seven logical schemas (sec, mst, inv, pur, sal,
  fin, app). They were removed in favour of flat [dbo] names; the slot is kept
  so the numbered sequence 00->N stays intact and existing installs are not
  renumbered. Nothing to create here now.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'01_Schemas.sql completed (no schemas - all objects live in dbo).';
GO
