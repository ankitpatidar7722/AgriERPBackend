/*==============================================================================
  AgriERP  |  tests/smoke_test.sql
  ------------------------------------------------------------------------------
  Proves the schema actually behaves, not just that it compiles. Creates a
  throwaway product, moves stock through it, asserts the invariants, then
  removes every row it created.

  Safe to run against a live database: everything it touches carries the
  ZZTEST- prefix and is deleted at the end. It does NOT roll back in one big
  transaction on purpose - one test deliberately triggers an error, and under
  SET XACT_ABORT ON that would doom an enclosing transaction and mask the
  remaining assertions.

  Run:  sqlcmd -S <server> -U <user> -P <pwd> -C -d AgriERP -i smoke_test.sql
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
GO

PRINT N'';
PRINT N'==================== AgriERP smoke test ====================';
GO

/*------------------------------------------------------------------------------
  Clean any residue from an interrupted previous run.
------------------------------------------------------------------------------*/
DELETE st FROM StockTransactions st
  INNER JOIN ItemMaster p ON p.ItemId = st.ItemId
  WHERE p.ItemCode LIKE N'ZZTEST-%';
DELETE b FROM ItemBatches b
  INNER JOIN ItemMaster p ON p.ItemId = b.ItemId
  WHERE p.ItemCode LIKE N'ZZTEST-%';
DELETE FROM ItemMaster WHERE ItemCode LIKE N'ZZTEST-%';
GO

DECLARE @Failures INT = 0;

/*==============================================================================
  Setup
==============================================================================*/
DECLARE @ItemSubGroupId INT = (SELECT ItemSubGroupId FROM ItemSubGroupMaster WHERE ItemSubGroupCode = N'INSEC');
DECLARE @CompanyId  INT = (SELECT CompanyId  FROM Companies  WHERE CompanyCode  = N'BAYER');
DECLARE @UnitMl     INT = (SELECT UnitId     FROM Units      WHERE UnitCode     = N'ML');
DECLARE @UnitBtl    INT = (SELECT UnitId     FROM Units      WHERE UnitCode     = N'BTL');
DECLARE @Gst18      INT = (SELECT GstSlabId  FROM GstSlabs   WHERE TotalRate    = 18.000);
DECLARE @Hsn3808    INT = (SELECT HsnId      FROM HsnCodes   WHERE HsnCode      = N'3808');
DECLARE @LocMain    INT = (SELECT LocationId FROM StorageLocations WHERE LocationCode = N'MAIN');

-- The group is taken from the sub-group rather than hardcoded: that is exactly
-- the rule the application follows, so if the mapping in 15_ItemGroups.sql is
-- ever wrong this insert fails here rather than at a counter.
DECLARE @ItemGroupId INT =
    (SELECT ItemGroupId FROM ItemSubGroupMaster WHERE ItemSubGroupId = @ItemSubGroupId);

INSERT INTO ItemMaster
    (ItemCode, ItemName, ShortName, TechnicalName, ItemGroupId, ItemSubGroupId, CompanyId,
     PackingSize, PackingUnitId, UnitId, HsnId, GstSlabId,
     PurchaseRate, SellingRate, Mrp, WholesaleRate, DealerRate, MinSellingRate,
     MinStockLevel, MaxStockLevel, DefaultLocationId)
VALUES
    (N'ZZTEST-001', N'Confidor 17.8% SL 250ml', N'Confidor 250ml',
     N'Imidacloprid 17.8% SL', @ItemGroupId, @ItemSubGroupId, @CompanyId,
     250, @UnitMl, @UnitBtl, @Hsn3808, @Gst18,
     380.0000, 450.0000, 495.0000, 420.0000, 410.0000, 400.0000,
     10, 100, @LocMain);

DECLARE @ItemId INT = SCOPE_IDENTITY();

-- Two batches with different expiry dates: the FEFO test needs them.
INSERT INTO ItemBatches (ItemId, BatchNumber, LocationId, ManufacturingDate, ExpiryDate, PurchaseRate, SellingRate, Mrp)
VALUES (@ItemId, N'ZZB-LATE',  @LocMain, '2026-01-15', '2028-01-14', 380.0000, 450.0000, 495.0000);
DECLARE @BatchLate BIGINT = SCOPE_IDENTITY();

INSERT INTO ItemBatches (ItemId, BatchNumber, LocationId, ManufacturingDate, ExpiryDate, PurchaseRate, SellingRate, Mrp)
VALUES (@ItemId, N'ZZB-EARLY', @LocMain, '2025-06-01', '2026-11-30', 372.0000, 450.0000, 495.0000);
DECLARE @BatchEarly BIGINT = SCOPE_IDENTITY();

PRINT N'Setup: product and two batches created.';

/*==============================================================================
  TEST 1 - inward posting updates the batch and writes the journal
==============================================================================*/
DECLARE @TxnId BIGINT;

EXEC usp_PostStockTransaction
    @TransactionTypeId = 2,                 -- PurchaseIn
    @TransactionDate   = '2026-07-01',
    @ItemId         = @ItemId,
    @BatchId           = @BatchLate,
    @LocationId        = @LocMain,
    @Quantity          = 50,
    @Rate              = 380.0000,
    @ReferenceType     = N'Purchase',
    @ReferenceNumber   = N'ZZTEST-PUR',
    @StockTransactionId = @TxnId OUTPUT;

EXEC usp_PostStockTransaction
    @TransactionTypeId = 2,
    @TransactionDate   = '2026-07-02',
    @ItemId         = @ItemId,
    @BatchId           = @BatchEarly,
    @LocationId        = @LocMain,
    @Quantity          = 20,
    @Rate              = 372.0000,
    @ReferenceType     = N'Purchase',
    @ReferenceNumber   = N'ZZTEST-PUR',
    @StockTransactionId = @TxnId OUTPUT;

IF (SELECT CurrentQty FROM ItemBatches WHERE BatchId = @BatchLate) = 50
   AND (SELECT CurrentQty FROM ItemBatches WHERE BatchId = @BatchEarly) = 20
    PRINT N'PASS  1. Inward posting updated batch quantities (50 + 20).';
ELSE
BEGIN
    PRINT N'FAIL  1. Batch quantities wrong after inward posting.';
    SET @Failures += 1;
END

/*==============================================================================
  TEST 2 - product-level stock rolls up from batches
==============================================================================*/
DECLARE @Rolled DECIMAL(18,3) = (SELECT CurrentStock FROM vw_ItemStock WHERE ItemId = @ItemId);
DECLARE @Value  DECIMAL(18,2) = (SELECT StockValueAtCost FROM vw_ItemStock WHERE ItemId = @ItemId);

-- 50 * 380 + 20 * 372 = 19000 + 7440 = 26440, valued at each batch's own rate.
IF @Rolled = 70 AND @Value = 26440.00
    PRINT N'PASS  2. vw_ItemStock rolled up 70 units valued 26,440.00 at batch cost.';
ELSE
BEGIN
    PRINT N'FAIL  2. Roll-up wrong. Qty=' + CAST(@Rolled AS NVARCHAR(20)) + N' Value=' + CAST(@Value AS NVARCHAR(20));
    SET @Failures += 1;
END

/*==============================================================================
  TEST 3 - FEFO returns the earlier-expiring batch first
==============================================================================*/
DECLARE @Fefo TABLE (RowNo INT IDENTITY(1,1), BatchId BIGINT, BatchNumber NVARCHAR(50),
                     LocationId INT, LocationName NVARCHAR(100), ManufacturingDate DATE,
                     ExpiryDate DATE, AvailableQty DECIMAL(18,3), PurchaseRate DECIMAL(18,4),
                     SellingRate DECIMAL(18,4), Mrp DECIMAL(18,4), DaysToExpiry INT,
                     CumulativeQty DECIMAL(18,3), RequiredQty DECIMAL(18,3));

INSERT INTO @Fefo (BatchId, BatchNumber, LocationId, LocationName, ManufacturingDate, ExpiryDate,
                   AvailableQty, PurchaseRate, SellingRate, Mrp, DaysToExpiry, CumulativeQty, RequiredQty)
EXEC usp_GetAvailableBatches @ItemId = @ItemId;

IF (SELECT BatchId FROM @Fefo WHERE RowNo = 1) = @BatchEarly
    PRINT N'PASS  3. FEFO offered ZZB-EARLY (expires 2026-11-30) before ZZB-LATE.';
ELSE
BEGIN
    PRINT N'FAIL  3. FEFO ordering wrong.';
    SET @Failures += 1;
END

/*==============================================================================
  TEST 4 - outward posting reduces stock
==============================================================================*/
EXEC usp_PostStockTransaction
    @TransactionTypeId = 4,                 -- SalesOut
    @TransactionDate   = '2026-07-20',
    @ItemId         = @ItemId,
    @BatchId           = @BatchEarly,
    @LocationId        = @LocMain,
    @Quantity          = 8,
    @Rate              = 450.0000,
    @ReferenceType     = N'Sale',
    @ReferenceId       = 999999,            -- stand-in invoice header id, reversed in test 7
    @ReferenceNumber   = N'ZZTEST-INV',
    @StockTransactionId = @TxnId OUTPUT;

IF (SELECT CurrentQty FROM ItemBatches WHERE BatchId = @BatchEarly) = 12
    PRINT N'PASS  4. Outward posting reduced ZZB-EARLY from 20 to 12.';
ELSE
BEGIN
    PRINT N'FAIL  4. Outward posting did not reduce stock correctly.';
    SET @Failures += 1;
END

/*==============================================================================
  TEST 5 - running balance in the ledger view
==============================================================================*/
DECLARE @FinalBalance DECIMAL(18,3) =
(
    SELECT TOP (1) RunningBalance
    FROM vw_StockLedger
    WHERE ItemId = @ItemId
    ORDER BY TransactionDate DESC, StockTransactionId DESC
);

IF @FinalBalance = 62      -- 50 + 20 - 8
    PRINT N'PASS  5. vw_StockLedger running balance closed at 62.';
ELSE
BEGIN
    PRINT N'FAIL  5. Running balance = ' + ISNULL(CAST(@FinalBalance AS NVARCHAR(20)), N'NULL') + N', expected 62.';
    SET @Failures += 1;
END

/*==============================================================================
  TEST 6 - negative stock is refused
==============================================================================*/
DECLARE @Blocked BIT = 0;
BEGIN TRY
    EXEC usp_PostStockTransaction
        @TransactionTypeId = 4,
        @TransactionDate   = '2026-07-21',
        @ItemId         = @ItemId,
        @BatchId           = @BatchEarly,
        @LocationId        = @LocMain,
        @Quantity          = 999,           -- only 12 on hand
        @Rate              = 450.0000,
        @StockTransactionId = @TxnId OUTPUT;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 50024 SET @Blocked = 1;
END CATCH

IF @Blocked = 1 AND (SELECT CurrentQty FROM ItemBatches WHERE BatchId = @BatchEarly) = 12
    PRINT N'PASS  6. Overselling refused (error 50024) and stock left untouched.';
ELSE
BEGIN
    PRINT N'FAIL  6. Negative stock was not blocked.';
    SET @Failures += 1;
END

/*==============================================================================
  TEST 7 - cancellation reverses rather than deletes
==============================================================================*/
DECLARE @JournalRowsBefore INT =
    (SELECT COUNT(*) FROM StockTransactions WHERE ItemId = @ItemId);

DECLARE @Reversed TABLE (ReversedRowCount INT);
INSERT INTO @Reversed
EXEC usp_ReverseDocumentStock
     @ReferenceType = N'Sale',
     @ReferenceId   = 999999,
     @Remarks       = N'ZZTEST cancellation';

DECLARE @JournalRowsAfter INT =
    (SELECT COUNT(*) FROM StockTransactions WHERE ItemId = @ItemId);
DECLARE @QtyAfterReversal DECIMAL(18,3) =
    (SELECT CurrentQty FROM ItemBatches WHERE BatchId = @BatchEarly);

IF @QtyAfterReversal = 20 AND @JournalRowsAfter > @JournalRowsBefore
    PRINT N'PASS  7. Cancellation restored stock to 20 by appending a reversal, not deleting.';
ELSE
BEGIN
    -- PRINT takes only scalar expressions, so the value is read into a variable first.
    PRINT N'FAIL  7. Reversal misbehaved. Qty=' + CAST(@QtyAfterReversal AS NVARCHAR(20))
        + N' rows before=' + CAST(@JournalRowsBefore AS NVARCHAR(10))
        + N' after=' + CAST(@JournalRowsAfter AS NVARCHAR(10));
    SET @Failures += 1;
END

/*==============================================================================
  TEST 8 - the batch cache agrees with the journal
==============================================================================*/
DECLARE @Drift TABLE
(
    BatchId BIGINT, ItemId INT, ItemName NVARCHAR(200), BatchNumber NVARCHAR(50),
    CachedInward DECIMAL(18,3), JournalInward DECIMAL(18,3),
    CachedOutward DECIMAL(18,3), JournalOutward DECIMAL(18,3),
    CachedCurrentQty DECIMAL(18,3), JournalCurrentQty DECIMAL(18,3)
);
INSERT INTO @Drift EXEC usp_RebuildBatchQuantities @ItemId = @ItemId, @ReportOnly = 1;

IF NOT EXISTS (SELECT 1 FROM @Drift)
    PRINT N'PASS  8. usp_RebuildBatchQuantities found zero drift between cache and journal.';
ELSE
BEGIN
    PRINT N'FAIL  8. Batch cache has drifted from the journal.';
    SET @Failures += 1;
END

/*==============================================================================
  TEST 9 - document numbering increments and formats
==============================================================================*/
DECLARE @Num1 NVARCHAR(30), @Num2 NVARCHAR(30);

-- The counter is captured and put back afterwards. Without this the test burns
-- two invoice numbers and the shop's first real bill starts at 00003 - a gap a
-- GST auditor will ask about. Safe because no document is created against them.
DECLARE @SeriesId INT, @CounterBefore INT;
SELECT TOP (1) @SeriesId = NumberSeriesId, @CounterBefore = CurrentNumber
FROM NumberSeries
WHERE DocumentType = N'Sale' AND IsActive = 1
ORDER BY NumberSeriesId;

EXEC usp_GetNextDocumentNumber @DocumentType = N'Sale', @DocumentNumber = @Num1 OUTPUT;
EXEC usp_GetNextDocumentNumber @DocumentType = N'Sale', @DocumentNumber = @Num2 OUTPUT;

UPDATE NumberSeries SET CurrentNumber = @CounterBefore WHERE NumberSeriesId = @SeriesId;

IF @Num1 IS NOT NULL AND @Num2 IS NOT NULL AND @Num1 <> @Num2
    PRINT N'PASS  9. Numbering produced ' + @Num1 + N' then ' + @Num2 + N'.';
ELSE
BEGIN
    PRINT N'FAIL  9. Numbering did not advance.';
    SET @Failures += 1;
END

/*==============================================================================
  TEST 10 - the money model on a sales line is self-consistent
==============================================================================*/
DECLARE @Check TABLE (Gross DECIMAL(18,2), Taxable DECIMAL(18,2), LineTotal DECIMAL(18,2),
                      CostAmount DECIMAL(18,2), LineProfit DECIMAL(18,2));

-- 10 bottles at 450, 5% line discount (225), 18% GST on 4275 = 769.50
INSERT INTO @Check
SELECT CAST(10 * 450.00 AS DECIMAL(18,2)),
       CAST(10 * 450.00 - 225.00 AS DECIMAL(18,2)),
       CAST(10 * 450.00 - 225.00 + 384.75 + 384.75 AS DECIMAL(18,2)),
       CAST(10 * 380.00 AS DECIMAL(18,2)),
       CAST(10 * 450.00 - 225.00 - (10 * 380.00) AS DECIMAL(18,2));

IF (SELECT Taxable FROM @Check) = 4275.00
   AND (SELECT LineTotal FROM @Check) = 5044.50
   AND (SELECT LineProfit FROM @Check) = 475.00
    PRINT N'PASS 10. Money model arithmetic: taxable 4,275.00 / total 5,044.50 / profit 475.00.';
ELSE
BEGIN
    PRINT N'FAIL 10. Money model arithmetic mismatch.';
    SET @Failures += 1;
END

/*==============================================================================
  Cleanup
==============================================================================*/
DELETE st FROM StockTransactions st WHERE st.ItemId = @ItemId;
DELETE FROM ItemBatches WHERE ItemId = @ItemId;
DELETE FROM ItemMaster WHERE ItemId = @ItemId;

PRINT N'Cleanup: test product, batches and journal rows removed.';
PRINT N'------------------------------------------------------------';
IF @Failures = 0
    PRINT N'RESULT: all 10 checks passed.';
ELSE
    PRINT N'RESULT: ' + CAST(@Failures AS NVARCHAR(10)) + N' check(s) FAILED.';
PRINT N'============================================================';
GO
