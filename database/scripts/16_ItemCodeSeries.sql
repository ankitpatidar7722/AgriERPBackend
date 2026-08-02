/*==============================================================================
  AgriERP  |  16_ItemCodeSeries.sql        (step 2b - one code series per group)
  ------------------------------------------------------------------------------
  Every item group counts on its own, with a single-letter prefix:

      Product Master      P-000001
      Fertilizers Master  F-000001
      Seed Master         S-000001
      Other Master        R-000001

  Other is R, not O: a capital O next to a zero in a six-digit code is the
  classic misread on a handwritten stock sheet, and P-000001 / O-000001 differ
  by one glyph that is hard to tell apart in most counter-printer fonts.

  WHY EXISTING ITEMS ARE RECODED
  ------------------------------
  Codes were issued from one shared "Product" series, so the seven items on hand
  are PRD-000001..PRD-000007 regardless of what they are. Leaving those beside
  newly issued S-000001 would mean the code stops telling you what the item is,
  which is the entire reason for a per-group series.

  Recoding is safe here and would NOT be later: SalesDetails and every other
  transaction line reference ItemId, never ItemCode, so no document is orphaned
  and no total changes. Once bills carrying the old code have gone out to
  customers this stops being true - at that point the honest move is to leave
  history alone and let only new items take the new form.

  Idempotent: prefixes are set to a known value and codes are only rewritten
  when they do not already match their group.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT N'--- 16_ItemCodeSeries ---';
GO

/*==============================================================================
  1. Single-letter prefixes
==============================================================================*/
UPDATE ItemGroupMaster SET ItemCodePrefix = N'P' WHERE ItemGroupCode = N'PRDGRP' AND ItemCodePrefix <> N'P';
UPDATE ItemGroupMaster SET ItemCodePrefix = N'F' WHERE ItemGroupCode = N'FRTGRP' AND ItemCodePrefix <> N'F';
UPDATE ItemGroupMaster SET ItemCodePrefix = N'S' WHERE ItemGroupCode = N'SEDGRP' AND ItemCodePrefix <> N'S';
UPDATE ItemGroupMaster SET ItemCodePrefix = N'R' WHERE ItemGroupCode = N'OTHGRP' AND ItemCodePrefix <> N'R';
GO

-- Keep the series rows in step with the group they belong to.
UPDATE ns
SET Prefix = g.ItemCodePrefix
FROM NumberSeries ns
JOIN ItemGroupMaster g ON ns.DocumentType = N'Item_' + g.ItemGroupCode
WHERE ns.Prefix <> g.ItemCodePrefix;
GO

/*==============================================================================
  2. Recode the items that already exist

  Numbered within each group by creation order, so the oldest fertilizer is
  F-000001 rather than keeping an arbitrary slot from the shared series.
==============================================================================*/
IF EXISTS (
    SELECT 1
    FROM ItemMaster i
    JOIN ItemGroupMaster g ON g.ItemGroupId = i.ItemGroupId
    WHERE i.IsDeleted = 0 AND i.ItemCode NOT LIKE g.ItemCodePrefix + N'-%')
BEGIN
    ;WITH numbered AS (
        SELECT i.ItemId,
               g.ItemCodePrefix,
               ROW_NUMBER() OVER (PARTITION BY i.ItemGroupId ORDER BY i.ItemId) AS Seq
        FROM ItemMaster i
        JOIN ItemGroupMaster g ON g.ItemGroupId = i.ItemGroupId
        WHERE i.IsDeleted = 0
    )
    UPDATE i
    SET ItemCode = n.ItemCodePrefix + N'-' + RIGHT(N'000000' + CAST(n.Seq AS NVARCHAR(10)), 6),
        UpdatedAt = SYSUTCDATETIME()
    FROM ItemMaster i
    JOIN numbered n ON n.ItemId = i.ItemId;

    PRINT N'Recoded ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' item(s) onto their group series.';
END
ELSE
    PRINT N'Item codes already match their group prefixes.';
GO

/*==============================================================================
  3. Point each counter at the highest number it has issued

  Never backwards: the counter only moves forward, and rewinding it past a code
  already in use makes the next save collide on UQ_ItemMaster_ItemCode.
==============================================================================*/
UPDATE ns
SET CurrentNumber = x.HighestIssued
FROM NumberSeries ns
JOIN ItemGroupMaster g ON ns.DocumentType = N'Item_' + g.ItemGroupCode
CROSS APPLY (
    SELECT ISNULL(MAX(TRY_CONVERT(INT, RIGHT(i.ItemCode, 6))), 0) AS HighestIssued
    FROM ItemMaster i
    WHERE i.ItemGroupId = g.ItemGroupId
      AND i.IsDeleted = 0
      AND i.ItemCode LIKE g.ItemCodePrefix + N'-%'
) x
WHERE ns.CurrentNumber < x.HighestIssued;
GO

/*==============================================================================
  4. Retire the shared series

  Left in place but deactivated rather than deleted: if anything still asks for
  DocumentType 'Product' the procedure now returns nothing and the caller fails
  loudly, which is what should happen. Deleting the row would look the same but
  lose the evidence that it ever existed.
==============================================================================*/
UPDATE NumberSeries SET IsActive = 0 WHERE DocumentType = N'Product' AND IsActive = 1;
GO

/*==============================================================================
  5. VERIFY
==============================================================================*/
DECLARE @problems INT = 0, @n INT;

SET @n = (
    SELECT COUNT(*) FROM ItemMaster i
    JOIN ItemGroupMaster g ON g.ItemGroupId = i.ItemGroupId
    WHERE i.IsDeleted = 0 AND i.ItemCode NOT LIKE g.ItemCodePrefix + N'-%');
IF @n > 0 BEGIN PRINT N'FAIL: ' + CAST(@n AS NVARCHAR(10)) + N' item(s) carry a code from another group'; SET @problems += 1; END

SET @n = (SELECT COUNT(*) FROM (SELECT ItemCode FROM ItemMaster WHERE IsDeleted = 0
                                GROUP BY ItemCode HAVING COUNT(*) > 1) d);
IF @n > 0 BEGIN PRINT N'FAIL: ' + CAST(@n AS NVARCHAR(10)) + N' duplicate item code(s)'; SET @problems += 1; END

SET @n = (SELECT COUNT(*) FROM NumberSeries ns
          JOIN ItemGroupMaster g ON ns.DocumentType = N'Item_' + g.ItemGroupCode
          WHERE ns.IsActive = 1);
IF @n <> 4 BEGIN PRINT N'FAIL: expected 4 active item series, found ' + CAST(@n AS NVARCHAR(10)); SET @problems += 1; END

IF @problems = 0
BEGIN
    PRINT N'RESULT: 16_ItemCodeSeries completed - all checks passed.';
    SELECT N'  ' + g.ItemGroupName + N'  prefix=' + g.ItemCodePrefix
         + N'  next=' + g.ItemCodePrefix + N'-'
         + RIGHT(N'000000' + CAST(ns.CurrentNumber + 1 AS NVARCHAR(10)), 6)
         + N'  existing=' + ISNULL(STUFF((
               SELECT N', ' + i.ItemCode FROM ItemMaster i
               WHERE i.ItemGroupId = g.ItemGroupId AND i.IsDeleted = 0
               ORDER BY i.ItemCode FOR XML PATH(N''), TYPE).value(N'.', N'NVARCHAR(MAX)'), 1, 2, N''), N'(none)')
    FROM ItemGroupMaster g
    JOIN NumberSeries ns ON ns.DocumentType = N'Item_' + g.ItemGroupCode
    WHERE g.IsDeleted = 0 ORDER BY g.DisplayOrder;
END
ELSE
    PRINT N'RESULT: 16_ItemCodeSeries finished with ' + CAST(@problems AS NVARCHAR(10)) + N' problem(s).';
GO
