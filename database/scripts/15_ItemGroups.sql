/*==============================================================================
  AgriERP  |  15_ItemGroups.sql          (step 1b - the group layer)
  ------------------------------------------------------------------------------
  Item groups, and the per-group field definitions that drive the item form.

  THE IDEA
  --------
  A shop does not enter a pesticide the way it enters a seed. A pesticide needs
  a technical name and a CIB licence number; a seed needs a germination
  percentage and a lot number; a sprayer needs neither. One form with every
  field on it asks the operator to ignore two thirds of the screen.

      ItemGroupMaster        WHAT KIND of item      (4 rows)
      ItemGroupFieldMaster   WHICH FIELDS that kind shows, and how
      ItemSubGroupMaster     the finer classification within a group
      ItemMaster             the items themselves
      ItemMasterDetails      values for fields that have no column of their own

  WHERE VALUES LIVE - AND WHY NOT LIKE THE SOURCE SYSTEM
  -----------------------------------------------------
  The design this follows (IndusDemo) stores every field TWICE: once in a typed
  column on the item table and again as text in a details table. 177 items x 30
  fields there means 5,310 duplicated rows with nothing keeping the two copies
  in step.

  Here a field is stored in exactly one place, and IsStoredOnItem says which:

      IsStoredOnItem = 1   the value lives in the ItemMaster COLUMN named by
                           FieldName. Rates, GST, stock levels - everything
                           billing, FEFO and GST reporting depend on - keep
                           their real types, CHECK constraints and foreign keys.

      IsStoredOnItem = 0   the value lives in ItemMasterDetails as text. Only
                           group-specific extras land here, where a typed
                           column would be NULL for three groups out of four.

  TWO MORE DEPARTURES FROM THE SOURCE SYSTEM, BOTH DELIBERATE
  -----------------------------------------------------------
  1. ItemMasterDetails.ItemGroupFieldId is a REAL foreign key. In the source it
     is a column called FieldID that is 0 on every one of its 51,694 rows, so
     the only link back to the definition is a field NAME matched as a string -
     rename a field there and every stored value is orphaned silently.

  2. A lookup list is named (LookupSource = 'units'), not stored as executable
     SQL. The source keeps SELECT statements in a table column and concatenates
     them at runtime; that is unversioned code living in data, and an injection
     surface. The API resolves the name against a fixed whitelist instead.

  Idempotent: safe to run more than once.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 15_ItemGroups ---';
GO

/*==============================================================================
  1. ItemGroupMaster
==============================================================================*/
IF OBJECT_ID(N'ItemGroupMaster', N'U') IS NULL
BEGIN
    CREATE TABLE ItemGroupMaster
    (
        ItemGroupId     INT             IDENTITY(1,1) NOT NULL,
        ItemGroupCode   NVARCHAR(20)    NOT NULL,
        ItemGroupName   NVARCHAR(100)   NOT NULL,
        -- Feeds the item code: PRD-000001, FRT-000001, SED-000001, OTH-000001.
        -- Each group counts separately, so a seed and a pesticide never share
        -- a number and the code says what the item is at a glance.
        ItemCodePrefix  NVARCHAR(10)    NOT NULL,
        Description     NVARCHAR(300)   NULL,
        IconName        NVARCHAR(50)    NULL,
        DisplayOrder    INT             NOT NULL CONSTRAINT DF_ItemGroupMaster_DisplayOrder DEFAULT (0),

        IsActive        BIT             NOT NULL CONSTRAINT DF_ItemGroupMaster_IsActive  DEFAULT (1),
        IsDeleted       BIT             NOT NULL CONSTRAINT DF_ItemGroupMaster_IsDeleted DEFAULT (0),
        CreatedAt       DATETIME2(3)    NOT NULL CONSTRAINT DF_ItemGroupMaster_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        UpdatedBy       INT             NULL,
        RowVersion      ROWVERSION      NOT NULL,

        CONSTRAINT PK_ItemGroupMaster PRIMARY KEY CLUSTERED (ItemGroupId),
        CONSTRAINT CK_ItemGroupMaster_Prefix CHECK (LEN(LTRIM(RTRIM(ItemCodePrefix))) > 0)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UQ_ItemGroupMaster_Code
        ON ItemGroupMaster (ItemGroupCode) WHERE IsDeleted = 0;
    CREATE UNIQUE NONCLUSTERED INDEX UQ_ItemGroupMaster_Prefix
        ON ItemGroupMaster (ItemCodePrefix) WHERE IsDeleted = 0;
END
GO

/*==============================================================================
  2. ItemGroupFieldMaster
==============================================================================*/
IF OBJECT_ID(N'ItemGroupFieldMaster', N'U') IS NULL
BEGIN
    CREATE TABLE ItemGroupFieldMaster
    (
        ItemGroupFieldId    INT             IDENTITY(1,1) NOT NULL,
        ItemGroupId         INT             NOT NULL,

        -- When IsStoredOnItem = 1 this MUST be a real column name on
        -- ItemMaster; the API refuses to save a definition that names a
        -- column which does not exist, so a typo cannot become a dead field.
        FieldName           NVARCHAR(128)   NOT NULL,
        FieldDisplayName    NVARCHAR(100)   NOT NULL,   -- the label on screen
        HelpText            NVARCHAR(300)   NULL,

        -- text | number | decimal | select | checkbox | date | textarea
        FieldType           NVARCHAR(20)    NOT NULL,
        IsStoredOnItem      BIT             NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_Stored DEFAULT (1),

        IsDisplay           BIT             NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_IsDisplay  DEFAULT (1),
        IsRequired          BIT             NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_IsRequired DEFAULT (0),
        IsReadOnly          BIT             NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_IsReadOnly DEFAULT (0),

        DisplayOrder        INT             NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_Order DEFAULT (0),
        -- How many of the form's four columns this field occupies.
        ColumnSpan          TINYINT         NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_Span  DEFAULT (1),
        SectionName         NVARCHAR(60)    NULL,       -- groups fields under a heading

        DefaultValue        NVARCHAR(200)   NULL,
        -- A NAME the API resolves against a whitelist - never SQL text.
        LookupSource        NVARCHAR(60)    NULL,
        MinValue            DECIMAL(18,4)   NULL,
        MaxValue            DECIMAL(18,4)   NULL,
        MaxLength           INT             NULL,
        UnitLabel           NVARCHAR(30)    NULL,       -- shown after the input, e.g. "kg"

        IsActive            BIT             NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_IsActive  DEFAULT (1),
        IsDeleted           BIT             NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_IsDeleted DEFAULT (0),
        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_ItemGroupFieldMaster_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,
        RowVersion          ROWVERSION      NOT NULL,

        CONSTRAINT PK_ItemGroupFieldMaster PRIMARY KEY CLUSTERED (ItemGroupFieldId),
        CONSTRAINT FK_ItemGroupFieldMaster_Group
            FOREIGN KEY (ItemGroupId) REFERENCES ItemGroupMaster (ItemGroupId),
        CONSTRAINT CK_ItemGroupFieldMaster_Type CHECK
            (FieldType IN (N'text', N'number', N'decimal', N'select', N'checkbox', N'date', N'textarea')),
        CONSTRAINT CK_ItemGroupFieldMaster_Span  CHECK (ColumnSpan BETWEEN 1 AND 4),
        CONSTRAINT CK_ItemGroupFieldMaster_Range CHECK
            (MinValue IS NULL OR MaxValue IS NULL OR MaxValue >= MinValue),
        -- A select must say where its options come from.
        CONSTRAINT CK_ItemGroupFieldMaster_Lookup CHECK
            (FieldType <> N'select' OR LookupSource IS NOT NULL)
    );

    -- One definition per field per group.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_ItemGroupFieldMaster_Group_Field
        ON ItemGroupFieldMaster (ItemGroupId, FieldName) WHERE IsDeleted = 0;

    CREATE NONCLUSTERED INDEX IX_ItemGroupFieldMaster_Group_Order
        ON ItemGroupFieldMaster (ItemGroupId, DisplayOrder)
        INCLUDE (FieldName, FieldDisplayName, FieldType, IsStoredOnItem, IsRequired)
        WHERE IsDeleted = 0 AND IsDisplay = 1;
END
GO

/*==============================================================================
  3. ItemMasterDetails  - values for fields with no column of their own
==============================================================================*/
IF OBJECT_ID(N'ItemMasterDetails', N'U') IS NULL
BEGIN
    CREATE TABLE ItemMasterDetails
    (
        ItemDetailId        BIGINT          IDENTITY(1,1) NOT NULL,
        ItemId              INT             NOT NULL,
        -- A real foreign key. The system this pattern comes from leaves the
        -- equivalent column at 0 on every row and matches on the field name
        -- instead, which orphans every value the moment a field is renamed.
        ItemGroupFieldId    INT             NOT NULL,
        FieldValue          NVARCHAR(500)   NULL,

        CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_ItemMasterDetails_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy           INT             NULL,
        UpdatedAt           DATETIME2(3)    NULL,
        UpdatedBy           INT             NULL,

        CONSTRAINT PK_ItemMasterDetails PRIMARY KEY CLUSTERED (ItemDetailId),
        CONSTRAINT FK_ItemMasterDetails_Item
            FOREIGN KEY (ItemId) REFERENCES ItemMaster (ItemId) ON DELETE CASCADE,
        CONSTRAINT FK_ItemMasterDetails_Field
            FOREIGN KEY (ItemGroupFieldId) REFERENCES ItemGroupFieldMaster (ItemGroupFieldId)
    );

    -- One value per item per field. Without this a save that runs twice leaves
    -- two answers to the same question and no way to tell which is current.
    CREATE UNIQUE NONCLUSTERED INDEX UQ_ItemMasterDetails_Item_Field
        ON ItemMasterDetails (ItemId, ItemGroupFieldId);
END
GO

/*==============================================================================
  4. ItemGroupId on the two existing masters

  Added NULLable, backfilled, then tightened to NOT NULL - the only order that
  works on a table that already holds rows.
==============================================================================*/
IF COL_LENGTH(N'ItemSubGroupMaster', N'ItemGroupId') IS NULL
    ALTER TABLE ItemSubGroupMaster ADD ItemGroupId INT NULL;
GO
IF COL_LENGTH(N'ItemMaster', N'ItemGroupId') IS NULL
    ALTER TABLE ItemMaster ADD ItemGroupId INT NULL;
GO

/*==============================================================================
  5. The four groups
==============================================================================*/
MERGE ItemGroupMaster AS tgt
USING (VALUES
    (N'PRDGRP', N'Product Master',     N'PRD', N'Insecticides, pesticides, fungicides, herbicides and growth regulators.', N'SprayCan', 1),
    (N'FRTGRP', N'Fertilizers Master', N'FRT', N'Straight and bio fertilizers, micronutrients.',                            N'Leaf',     2),
    (N'SEDGRP', N'Seed Master',        N'SED', N'Vegetable, field crop, flower and fruit seed.',                            N'Sprout',   3),
    (N'OTHGRP', N'Other Master',       N'OTH', N'Equipment, accessories and anything that is not an input.',                N'Wrench',   4)
) AS src (ItemGroupCode, ItemGroupName, ItemCodePrefix, Description, IconName, DisplayOrder)
    ON tgt.ItemGroupCode = src.ItemGroupCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ItemGroupCode, ItemGroupName, ItemCodePrefix, Description, IconName, DisplayOrder)
    VALUES (src.ItemGroupCode, src.ItemGroupName, src.ItemCodePrefix, src.Description, src.IconName, src.DisplayOrder)
WHEN MATCHED THEN UPDATE SET
    tgt.ItemGroupName = src.ItemGroupName,
    tgt.Description   = src.Description,
    tgt.IconName      = src.IconName,
    tgt.DisplayOrder  = src.DisplayOrder;
GO

/*==============================================================================
  6. Put the 16 existing sub-groups under the right group

  Only fills rows that have not been assigned yet, so a shop that has since
  moved a sub-group by hand does not have it moved back on the next run.
==============================================================================*/
UPDATE sg
SET ItemGroupId = g.ItemGroupId
FROM ItemSubGroupMaster sg
CROSS APPLY (
    SELECT TOP 1 gm.ItemGroupId
    FROM ItemGroupMaster gm
    WHERE gm.ItemGroupCode =
        CASE
            WHEN sg.ItemSubGroupCode IN (N'INSEC', N'PESTI', N'FUNGI', N'HERBI', N'PGR')     THEN N'PRDGRP'
            WHEN sg.ItemSubGroupCode IN (N'FERT', N'BIOFERT', N'MICRO')                      THEN N'FRTGRP'
            WHEN sg.ItemSubGroupCode LIKE N'SEED%'                                            THEN N'SEDGRP'
            ELSE N'OTHGRP'
        END
) g
WHERE sg.ItemGroupId IS NULL;
GO

-- An item inherits its group from its sub-group.
UPDATE i
SET ItemGroupId = sg.ItemGroupId
FROM ItemMaster i
JOIN ItemSubGroupMaster sg ON sg.ItemSubGroupId = i.ItemSubGroupId
WHERE i.ItemGroupId IS NULL;
GO

/*==============================================================================
  7. Now that every row has a value, make it required
==============================================================================*/
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'ItemSubGroupMaster') AND name = 'ItemGroupId' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM ItemSubGroupMaster WHERE ItemGroupId IS NULL)
BEGIN
    ALTER TABLE ItemSubGroupMaster ALTER COLUMN ItemGroupId INT NOT NULL;
    ALTER TABLE ItemSubGroupMaster ADD CONSTRAINT FK_ItemSubGroupMaster_Group
        FOREIGN KEY (ItemGroupId) REFERENCES ItemGroupMaster (ItemGroupId);
    CREATE NONCLUSTERED INDEX IX_ItemSubGroupMaster_GroupId
        ON ItemSubGroupMaster (ItemGroupId)
        INCLUDE (ItemSubGroupCode, ItemSubGroupName, DisplayOrder) WHERE IsDeleted = 0;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'ItemMaster') AND name = 'ItemGroupId' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM ItemMaster WHERE ItemGroupId IS NULL)
BEGIN
    ALTER TABLE ItemMaster ALTER COLUMN ItemGroupId INT NOT NULL;
    ALTER TABLE ItemMaster ADD CONSTRAINT FK_ItemMaster_Group
        FOREIGN KEY (ItemGroupId) REFERENCES ItemGroupMaster (ItemGroupId);
    CREATE NONCLUSTERED INDEX IX_ItemMaster_GroupId
        ON ItemMaster (ItemGroupId)
        INCLUDE (ItemName, ItemSubGroupId, SellingRate, IsActive) WHERE IsDeleted = 0;
END
GO

/*==============================================================================
  8. Number series, one per group

  A seed and a pesticide must not share a running number, or the code stops
  telling you what the item is.
==============================================================================*/
MERGE NumberSeries AS tgt
USING (SELECT ItemGroupCode, ItemCodePrefix FROM ItemGroupMaster WHERE IsDeleted = 0) AS src
    ON tgt.DocumentType = N'Item_' + src.ItemGroupCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (DocumentType, Prefix, Suffix, Separator, PaddingLength, CurrentNumber, IncludeYearCode, IsActive)
    VALUES (N'Item_' + src.ItemGroupCode, src.ItemCodePrefix, N'', N'-', 6, 0, 0, 1);
GO

/*==============================================================================
  9. Field definitions
==============================================================================*/
DECLARE @Product INT = (SELECT ItemGroupId FROM ItemGroupMaster WHERE ItemGroupCode = N'PRDGRP');
DECLARE @Fert    INT = (SELECT ItemGroupId FROM ItemGroupMaster WHERE ItemGroupCode = N'FRTGRP');
DECLARE @Seed    INT = (SELECT ItemGroupId FROM ItemGroupMaster WHERE ItemGroupCode = N'SEDGRP');
DECLARE @Other   INT = (SELECT ItemGroupId FROM ItemGroupMaster WHERE ItemGroupCode = N'OTHGRP');

DECLARE @fields TABLE (
    ItemGroupId INT, FieldName NVARCHAR(128), FieldDisplayName NVARCHAR(100),
    FieldType NVARCHAR(20), IsStoredOnItem BIT, IsRequired BIT, DisplayOrder INT,
    ColumnSpan TINYINT, SectionName NVARCHAR(60), LookupSource NVARCHAR(60),
    HelpText NVARCHAR(300), UnitLabel NVARCHAR(30), MinValue DECIMAL(18,4), MaxValue DECIMAL(18,4));

/*---- shared by all four groups: the columns billing and stock depend on ----*/
INSERT INTO @fields (ItemGroupId, FieldName, FieldDisplayName, FieldType, IsStoredOnItem, IsRequired, DisplayOrder, ColumnSpan, SectionName, LookupSource, HelpText, UnitLabel, MinValue, MaxValue)
SELECT g.ItemGroupId, f.*
FROM ItemGroupMaster g
CROSS JOIN (VALUES
    (N'ItemName',        N'Item name',        N'text',     1, 1, 10, 1, N'Basic details', NULL,          NULL,                                          NULL, NULL, NULL),
    (N'ShortName',       N'Short name',       N'text',     1, 0, 20, 1, N'Basic details', NULL,          N'Printed on the invoice line.',               NULL, NULL, NULL),
    (N'ItemSubGroupId',  N'Sub group',        N'select',   1, 1, 30, 1, N'Basic details', N'subgroups',  NULL,                                          NULL, NULL, NULL),
    (N'CompanyId',       N'Company',          N'select',   1, 0, 40, 1, N'Basic details', N'companies',  NULL,                                          NULL, NULL, NULL),
    (N'Brand',           N'Brand',            N'text',     1, 0, 50, 1, N'Basic details', NULL,          NULL,                                          NULL, NULL, NULL),
    (N'PackingSize',     N'Pack size',        N'decimal',  1, 0, 60, 1, N'Basic details', NULL,          NULL,                                          NULL, 0,    NULL),
    (N'PackingUnitId',   N'Pack unit',        N'select',   1, 0, 70, 1, N'Basic details', N'units',      NULL,                                          NULL, NULL, NULL),
    (N'UnitId',          N'Selling unit',     N'select',   1, 1, 80, 1, N'Basic details', N'units',      NULL,                                          NULL, NULL, NULL),
    (N'Barcode',         N'Barcode',          N'text',     1, 0, 90, 1, N'Basic details', NULL,          NULL,                                          NULL, NULL, NULL),
    (N'RackNumber',      N'Rack',             N'text',     1, 0, 100, 1, N'Basic details', NULL,         NULL,                                          NULL, NULL, NULL),
    (N'Description',     N'Description',      N'textarea', 1, 0, 110, 4, N'Basic details', NULL,         NULL,                                          NULL, NULL, NULL),

    (N'HsnId',           N'HSN code',         N'select',   1, 0, 200, 1, N'Pricing & tax', N'hsncodes',  NULL,                                          NULL, NULL, NULL),
    (N'GstSlabId',       N'GST rate',         N'select',   1, 1, 210, 1, N'Pricing & tax', N'gstslabs',  NULL,                                          NULL, NULL, NULL),
    (N'Mrp',             N'MRP',              N'decimal',  1, 0, 220, 1, N'Pricing & tax', NULL,         NULL,                                          NULL, 0,    NULL),
    (N'PurchaseRate',    N'Purchase rate',    N'decimal',  1, 0, 230, 1, N'Pricing & tax', NULL,         N'Updated automatically when a purchase posts.', NULL, 0,  NULL),
    (N'SellingRate',     N'Selling rate',     N'decimal',  1, 0, 240, 1, N'Pricing & tax', NULL,         NULL,                                          NULL, 0,    NULL),
    (N'MinSellingRate',  N'Minimum rate',     N'decimal',  1, 0, 250, 1, N'Pricing & tax', NULL,         N'Going below needs a permission.',            NULL, 0,    NULL),
    (N'WholesaleRate',   N'Wholesale rate',   N'decimal',  1, 0, 260, 1, N'Pricing & tax', NULL,         NULL,                                          NULL, 0,    NULL),
    (N'DealerRate',      N'Dealer rate',      N'decimal',  1, 0, 270, 1, N'Pricing & tax', NULL,         NULL,                                          NULL, 0,    NULL),

    (N'MinStockLevel',   N'Minimum stock',    N'decimal',  1, 0, 300, 1, N'Stock', NULL, N'Triggers the low-stock alert.', NULL, 0, NULL),
    (N'MaxStockLevel',   N'Maximum stock',    N'decimal',  1, 0, 310, 1, N'Stock', NULL, NULL,                            NULL, 0, NULL),
    (N'ReorderLevel',    N'Reorder level',    N'decimal',  1, 0, 320, 1, N'Stock', NULL, NULL,                            NULL, 0, NULL),
    (N'IsBatchTracked',  N'Batch tracking',   N'checkbox', 1, 0, 330, 1, N'Stock', NULL, NULL,                            NULL, NULL, NULL),
    (N'IsExpiryTracked', N'Expiry tracking',  N'checkbox', 1, 0, 340, 1, N'Stock', NULL, N'Drives FEFO picking.',         NULL, NULL, NULL),
    (N'IsActive',        N'Active',           N'checkbox', 1, 0, 350, 1, N'Stock', NULL, NULL,                            NULL, NULL, NULL)
) AS f (FieldName, FieldDisplayName, FieldType, IsStoredOnItem, IsRequired, DisplayOrder, ColumnSpan, SectionName, LookupSource, HelpText, UnitLabel, MinValue, MaxValue)
WHERE g.IsDeleted = 0;

/*---- what makes each group different ----*/
INSERT INTO @fields (ItemGroupId, FieldName, FieldDisplayName, FieldType, IsStoredOnItem, IsRequired, DisplayOrder, ColumnSpan, SectionName, LookupSource, HelpText, UnitLabel, MinValue, MaxValue)
VALUES
    -- Product Master: a pesticide is a regulated chemical.
    (@Product, N'TechnicalName',   N'Technical name',    N'text',   1, 1, 120, 2, N'Basic details', NULL, N'Active ingredient, e.g. Imidacloprid 17.8% SL.', NULL, NULL, NULL),
    (@Product, N'LicenceNumber',   N'CIB licence no.',   N'text',   1, 1, 130, 1, N'Basic details', NULL, N'Shown to the inspector.',                        NULL, NULL, NULL),
    (@Product, N'AntidoteInfo',    N'Antidote / first aid', N'textarea', 0, 0, 140, 4, N'Safety',   NULL, N'Printed on the counter copy for poisoning cases.', NULL, NULL, NULL),
    (@Product, N'WaitingPeriod',   N'Pre-harvest interval', N'number', 0, 0, 150, 1, N'Safety',     NULL, N'Days between spraying and harvest.',             N'days', 0, 365),

    -- Fertilizers Master: bought and sold on nutrient content.
    (@Fert, N'NutrientContent',  N'N-P-K',             N'text',    0, 1, 120, 1, N'Composition', NULL, N'For example 46-0-0 for urea.',      NULL, NULL, NULL),
    (@Fert, N'FertilizerForm',   N'Form',              N'select',  0, 0, 130, 1, N'Composition', N'fertilizerform', NULL,                    NULL, NULL, NULL),
    (@Fert, N'SubsidyScheme',    N'Subsidy scheme',    N'text',    0, 0, 140, 1, N'Composition', NULL, N'Blank when sold at open market rate.', NULL, NULL, NULL),
    (@Fert, N'TechnicalName',    N'Chemical name',     N'text',    1, 0, 150, 1, N'Composition', NULL, NULL,                                  NULL, NULL, NULL),

    -- Seed Master: germination and lot are the legal minimum on a seed bag.
    (@Seed, N'SeedVariety',       N'Variety',          N'text',    0, 1, 120, 1, N'Seed details', NULL, NULL,                                 NULL, NULL, NULL),
    (@Seed, N'GerminationPct',    N'Germination',      N'decimal', 0, 1, 130, 1, N'Seed details', NULL, N'Minimum on the bag label.',         N'%', 0, 100),
    (@Seed, N'SeedLotNumber',     N'Lot number',       N'text',    0, 1, 140, 1, N'Seed details', NULL, NULL,                                 NULL, NULL, NULL),
    (@Seed, N'SeedTreatment',     N'Treatment',        N'text',    0, 0, 150, 1, N'Seed details', NULL, N'Fungicide the seed is dressed with.', NULL, NULL, NULL),
    (@Seed, N'SowingSeason',      N'Season',           N'select',  0, 0, 160, 1, N'Seed details', N'season', NULL,                             NULL, NULL, NULL),

    -- Other Master: equipment is a durable, not an input.
    (@Other, N'WarrantyMonths',   N'Warranty',         N'number',  0, 0, 120, 1, N'Equipment', NULL, NULL, N'months', 0, 240),
    (@Other, N'ModelNumber',      N'Model number',     N'text',    0, 0, 130, 1, N'Equipment', NULL, NULL, NULL, NULL, NULL);

/*---- upsert ----*/
MERGE ItemGroupFieldMaster AS tgt
USING @fields AS src
    ON tgt.ItemGroupId = src.ItemGroupId AND tgt.FieldName = src.FieldName
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ItemGroupId, FieldName, FieldDisplayName, FieldType, IsStoredOnItem, IsRequired,
            DisplayOrder, ColumnSpan, SectionName, LookupSource, HelpText, UnitLabel, MinValue, MaxValue)
    VALUES (src.ItemGroupId, src.FieldName, src.FieldDisplayName, src.FieldType, src.IsStoredOnItem, src.IsRequired,
            src.DisplayOrder, src.ColumnSpan, src.SectionName, src.LookupSource, src.HelpText, src.UnitLabel, src.MinValue, src.MaxValue)
WHEN MATCHED THEN UPDATE SET
    tgt.FieldDisplayName = src.FieldDisplayName,
    tgt.FieldType        = src.FieldType,
    tgt.IsStoredOnItem   = src.IsStoredOnItem,
    tgt.IsRequired       = src.IsRequired,
    tgt.DisplayOrder     = src.DisplayOrder,
    tgt.ColumnSpan       = src.ColumnSpan,
    tgt.SectionName      = src.SectionName,
    tgt.LookupSource     = src.LookupSource,
    tgt.HelpText         = src.HelpText,
    tgt.UnitLabel        = src.UnitLabel,
    tgt.MinValue         = src.MinValue,
    tgt.MaxValue         = src.MaxValue,
    tgt.UpdatedAt        = SYSUTCDATETIME();
GO

/*==============================================================================
  10. VERIFY

  The check that matters: every field claiming to be stored on the item must
  name a column that actually exists. Get this wrong and the form silently
  drops the value on save.
==============================================================================*/
DECLARE @problems INT = 0, @n INT;

SET @n = (SELECT COUNT(*) FROM ItemGroupMaster WHERE IsDeleted = 0);
IF @n <> 4 BEGIN PRINT N'FAIL: expected 4 item groups, found ' + CAST(@n AS NVARCHAR(10)); SET @problems += 1; END

SET @n = (SELECT COUNT(*) FROM ItemSubGroupMaster WHERE ItemGroupId IS NULL);
IF @n > 0 BEGIN PRINT N'FAIL: ' + CAST(@n AS NVARCHAR(10)) + N' sub-group(s) have no group'; SET @problems += 1; END

SET @n = (SELECT COUNT(*) FROM ItemMaster WHERE ItemGroupId IS NULL);
IF @n > 0 BEGIN PRINT N'FAIL: ' + CAST(@n AS NVARCHAR(10)) + N' item(s) have no group'; SET @problems += 1; END

SET @n = (
    SELECT COUNT(*) FROM ItemGroupFieldMaster f
    WHERE f.IsStoredOnItem = 1 AND f.IsDeleted = 0
      AND NOT EXISTS (SELECT 1 FROM sys.columns c
                      WHERE c.object_id = OBJECT_ID(N'ItemMaster') AND c.name = f.FieldName));
IF @n > 0
BEGIN
    PRINT N'FAIL: ' + CAST(@n AS NVARCHAR(10)) + N' field(s) claim an ItemMaster column that does not exist:';
    SELECT N'   ' + f.FieldName FROM ItemGroupFieldMaster f
    WHERE f.IsStoredOnItem = 1 AND f.IsDeleted = 0
      AND NOT EXISTS (SELECT 1 FROM sys.columns c
                      WHERE c.object_id = OBJECT_ID(N'ItemMaster') AND c.name = f.FieldName);
    SET @problems += 1;
END

IF @problems = 0
BEGIN
    PRINT N'RESULT: 15_ItemGroups completed - all checks passed.';
    SELECT N'  ' + g.ItemGroupName + N' (' + g.ItemCodePrefix + N')'
         + N'  fields=' + CAST((SELECT COUNT(*) FROM ItemGroupFieldMaster f WHERE f.ItemGroupId = g.ItemGroupId AND f.IsDeleted = 0) AS NVARCHAR(10))
         + N'  subgroups=' + CAST((SELECT COUNT(*) FROM ItemSubGroupMaster s WHERE s.ItemGroupId = g.ItemGroupId AND s.IsDeleted = 0) AS NVARCHAR(10))
         + N'  items=' + CAST((SELECT COUNT(*) FROM ItemMaster i WHERE i.ItemGroupId = g.ItemGroupId AND i.IsDeleted = 0) AS NVARCHAR(10))
    FROM ItemGroupMaster g WHERE g.IsDeleted = 0 ORDER BY g.DisplayOrder;
END
ELSE
    PRINT N'RESULT: 15_ItemGroups finished with ' + CAST(@problems AS NVARCHAR(10)) + N' problem(s).';
GO
