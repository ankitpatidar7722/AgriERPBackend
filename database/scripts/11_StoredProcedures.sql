/*==============================================================================
  AgriERP  |  11_StoredProcedures.sql
  ------------------------------------------------------------------------------
  Only the procedures that must live in the database:

    usp_GetNextDocumentNumber   concurrency-safe invoice numbering
    usp_PostStockTransaction    the single writer for stock movements
    usp_ReverseDocumentStock    cancellation, by reversal not deletion
    usp_RebuildBatchQuantities  rebuilds the batch cache from the journal
    usp_GetAvailableBatches     FEFO batch suggestion for billing
    usp_DashboardSummary        thirteen dashboard tiles in one round trip

  Everything else - CRUD, search, filtering, paging - belongs in the API where
  it can be unit tested. Business logic scattered between C# and T-SQL is how
  ERPs become unmaintainable; these six are here because correctness under
  concurrency genuinely requires the database to arbitrate.
==============================================================================*/

USE [AgriERP];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*==============================================================================
  usp_GetNextDocumentNumber

  Produces the next number for a document type, e.g. INV/2025-26/00042.

  The entire read-increment-return is ONE UPDATE statement. That matters: SQL
  Server takes an exclusive lock on the counter row for the duration of the
  statement, so two salesmen pressing Save simultaneously are serialised by the
  engine and receive 42 and 43. A SELECT-then-UPDATE would let both read 41.
==============================================================================*/
CREATE OR ALTER PROCEDURE usp_GetNextDocumentNumber
    @DocumentType     NVARCHAR(30),
    @FinancialYearId  INT            = NULL,
    @DocumentNumber   NVARCHAR(30)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @FinancialYearId IS NULL
        SELECT @FinancialYearId = FinancialYearId
        FROM FinancialYears
        WHERE IsActive = 1;

    DECLARE @Result TABLE
    (
        CurrentNumber   INT,
        Prefix          NVARCHAR(20),
        Suffix          NVARCHAR(20),
        Separator       NVARCHAR(3),
        PaddingLength   TINYINT,
        IncludeYearCode BIT,
        FinancialYearId INT
    );

    UPDATE ns
       SET ns.CurrentNumber = ns.CurrentNumber + 1,
           ns.UpdatedAt     = SYSUTCDATETIME()
    OUTPUT inserted.CurrentNumber, inserted.Prefix, inserted.Suffix, inserted.Separator,
           inserted.PaddingLength, inserted.IncludeYearCode, inserted.FinancialYearId
      INTO @Result
      FROM NumberSeries AS ns
     WHERE ns.NumberSeriesId =
     (
         -- Prefer the series bound to this financial year; fall back to the
         -- year-agnostic one if the shop chose not to reset that document type.
         SELECT TOP (1) inner_ns.NumberSeriesId
         FROM NumberSeries AS inner_ns
         WHERE inner_ns.DocumentType = @DocumentType
           AND inner_ns.IsActive = 1
           AND (inner_ns.FinancialYearId = @FinancialYearId OR inner_ns.FinancialYearId IS NULL)
         ORDER BY CASE WHEN inner_ns.FinancialYearId = @FinancialYearId THEN 0 ELSE 1 END,
                  inner_ns.NumberSeriesId
     );

    IF NOT EXISTS (SELECT 1 FROM @Result)
    BEGIN
        DECLARE @msg NVARCHAR(200) =
            N'No active number series is configured for document type ''' + @DocumentType + N'''.';
        THROW 50010, @msg, 1;
    END

    SELECT @DocumentNumber =
           r.Prefix
         + CASE WHEN r.IncludeYearCode = 1 AND fy.YearCode IS NOT NULL
                THEN r.Separator + fy.YearCode ELSE N'' END
         + r.Separator
         + RIGHT(REPLICATE(N'0', r.PaddingLength) + CAST(r.CurrentNumber AS NVARCHAR(12)), r.PaddingLength)
         + r.Suffix
    FROM @Result AS r
    LEFT JOIN FinancialYears AS fy
           ON fy.FinancialYearId = ISNULL(r.FinancialYearId, @FinancialYearId);
END
GO

/*==============================================================================
  usp_PostStockTransaction

  The ONLY supported way to move stock. It does three things atomically:
     1. verifies the movement is legal (negative-stock rule),
     2. updates the batch's running totals,
     3. appends the journal row.

  Doing these separately from application code is how stock and ledger drift
  apart. Keeping them in one procedure means they either all happen or none do.

  Enlists in the caller's transaction when there is one, so a whole invoice
  posts as a unit.
==============================================================================*/
CREATE OR ALTER PROCEDURE usp_PostStockTransaction
    @TransactionTypeId    TINYINT,
    @TransactionDate      DATETIME2(3),
    @ItemId            INT,
    @BatchId              BIGINT,
    @LocationId           INT,
    @Quantity             DECIMAL(18,3),
    @Rate                 DECIMAL(18,4)  = 0,
    @ReferenceType        NVARCHAR(30)   = NULL,
    @ReferenceId          BIGINT         = NULL,
    @ReferenceDetailId    BIGINT         = NULL,
    @ReferenceNumber      NVARCHAR(30)   = NULL,
    @Remarks              NVARCHAR(300)  = NULL,
    @FinancialYearId      INT            = NULL,
    @UserId               INT            = NULL,
    @StockTransactionId   BIGINT         OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Quantity IS NULL OR @Quantity <= 0
        THROW 50020, N'Stock quantity must be greater than zero.', 1;

    DECLARE @Direction SMALLINT;
    SELECT @Direction = Direction
    FROM TransactionTypes
    WHERE TransactionTypeId = @TransactionTypeId AND IsActive = 1;

    IF @Direction IS NULL
        THROW 50021, N'Unknown or inactive stock transaction type.', 1;

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END

    BEGIN TRY
        /* UPDLOCK taken here and held to the end of the transaction. Two
           concurrent sales of the last packet both reach this line; the second
           waits, re-reads the reduced quantity, and is correctly rejected. */
        DECLARE @AvailableQty      DECIMAL(18,3),
                @AllowNegative     BIT,
                @BatchItemId    INT,
                @ItemName       NVARCHAR(200);

        SELECT @AvailableQty   = b.CurrentQty,
               @BatchItemId = b.ItemId,
               @AllowNegative  = p.AllowNegativeStock,
               @ItemName    = p.ItemName
        FROM ItemBatches AS b WITH (UPDLOCK, ROWLOCK)
        INNER JOIN ItemMaster AS p ON p.ItemId = b.ItemId
        WHERE b.BatchId = @BatchId;

        IF @BatchItemId IS NULL
            THROW 50022, N'Batch not found.', 1;

        IF @BatchItemId <> @ItemId
            THROW 50023, N'The supplied batch does not belong to the supplied product.', 1;

        IF @Direction = -1 AND @AllowNegative = 0 AND @AvailableQty < @Quantity
        BEGIN
            DECLARE @stockMsg NVARCHAR(400) =
                N'Insufficient stock for ' + ISNULL(@ItemName, N'product')
                + N'. Available: ' + CAST(@AvailableQty AS NVARCHAR(20))
                + N', required: '  + CAST(@Quantity     AS NVARCHAR(20)) + N'.';
            THROW 50024, @stockMsg, 1;
        END

        UPDATE ItemBatches
           SET InwardQty  = InwardQty  + CASE WHEN @Direction =  1 THEN @Quantity ELSE 0 END,
               OutwardQty = OutwardQty + CASE WHEN @Direction = -1 THEN @Quantity ELSE 0 END,
               UpdatedAt  = SYSUTCDATETIME(),
               UpdatedBy  = @UserId
         WHERE BatchId = @BatchId;

        INSERT INTO StockTransactions
        (
            TransactionDate, TransactionTypeId, ItemId, BatchId, LocationId,
            Direction, Quantity, Rate,
            ReferenceType, ReferenceId, ReferenceDetailId, ReferenceNumber,
            Remarks, FinancialYearId, CreatedBy
        )
        VALUES
        (
            @TransactionDate, @TransactionTypeId, @ItemId, @BatchId, @LocationId,
            @Direction, @Quantity, @Rate,
            @ReferenceType, @ReferenceId, @ReferenceDetailId, @ReferenceNumber,
            @Remarks, @FinancialYearId, @UserId
        );

        SET @StockTransactionId = SCOPE_IDENTITY();

        IF @OwnTransaction = 1
            COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/*==============================================================================
  usp_ReverseDocumentStock

  Cancelling a posted invoice does NOT delete its stock rows. It writes an
  opposite row for each, linked back via ReversesTransactionId. The journal
  stays append-only, the audit trail survives, and stock returns to where it
  was. Deleting instead would leave last month's closing stock silently
  different from the figure already reported.
==============================================================================*/
CREATE OR ALTER PROCEDURE usp_ReverseDocumentStock
    @ReferenceType   NVARCHAR(30),
    @ReferenceId     BIGINT,
    @ReversalDate    DATETIME2(3)  = NULL,
    @Remarks         NVARCHAR(300) = NULL,
    @UserId          INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ReversalDate = ISNULL(@ReversalDate, SYSDATETIME());
    SET @Remarks      = ISNULL(@Remarks, N'Reversal of ' + @ReferenceType + N' #' + CAST(@ReferenceId AS NVARCHAR(20)));

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END

    BEGIN TRY
        DECLARE @Originals TABLE
        (
            StockTransactionId BIGINT,
            TransactionTypeId  TINYINT,
            ItemId          INT,
            BatchId            BIGINT,
            LocationId         INT,
            Direction          SMALLINT,
            Quantity           DECIMAL(18,3),
            Rate               DECIMAL(18,4),
            ReferenceNumber    NVARCHAR(30),
            FinancialYearId    INT
        );

        INSERT INTO @Originals
        SELECT st.StockTransactionId, st.TransactionTypeId, st.ItemId, st.BatchId,
               st.LocationId, st.Direction, st.Quantity, st.Rate, st.ReferenceNumber,
               st.FinancialYearId
        FROM StockTransactions AS st
        WHERE st.ReferenceType = @ReferenceType
          AND st.ReferenceId   = @ReferenceId
          -- Skip rows that were already reversed, so a double-cancel is a no-op
          -- rather than a doubling of stock.
          AND NOT EXISTS (
                SELECT 1 FROM StockTransactions AS rev
                WHERE rev.ReversesTransactionId = st.StockTransactionId);

        -- Put the quantity back on each batch, in the opposite direction.
        UPDATE b
           SET b.InwardQty  = b.InwardQty  + CASE WHEN o.Direction = -1 THEN o.Quantity ELSE 0 END,
               b.OutwardQty = b.OutwardQty + CASE WHEN o.Direction =  1 THEN o.Quantity ELSE 0 END,
               b.UpdatedAt  = SYSUTCDATETIME(),
               b.UpdatedBy  = @UserId
        FROM ItemBatches AS b
        INNER JOIN (
            SELECT BatchId,
                   SUM(CASE WHEN Direction = -1 THEN Quantity ELSE 0 END) AS Quantity,
                   -1 AS Direction
            FROM @Originals GROUP BY BatchId
            HAVING SUM(CASE WHEN Direction = -1 THEN Quantity ELSE 0 END) > 0
            UNION ALL
            SELECT BatchId,
                   SUM(CASE WHEN Direction = 1 THEN Quantity ELSE 0 END) AS Quantity,
                   1 AS Direction
            FROM @Originals GROUP BY BatchId
            HAVING SUM(CASE WHEN Direction = 1 THEN Quantity ELSE 0 END) > 0
        ) AS o ON o.BatchId = b.BatchId;

        INSERT INTO StockTransactions
        (
            TransactionDate, TransactionTypeId, ItemId, BatchId, LocationId,
            Direction, Quantity, Rate,
            ReferenceType, ReferenceId, ReferenceNumber,
            ReversesTransactionId, Remarks, FinancialYearId, CreatedBy
        )
        SELECT @ReversalDate, o.TransactionTypeId, o.ItemId, o.BatchId, o.LocationId,
               o.Direction * -1, o.Quantity, o.Rate,
               @ReferenceType, @ReferenceId, o.ReferenceNumber,
               o.StockTransactionId, @Remarks, o.FinancialYearId, @UserId
        FROM @Originals AS o;

        SELECT @@ROWCOUNT AS ReversedRowCount;

        IF @OwnTransaction = 1
            COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/*==============================================================================
  usp_RebuildBatchQuantities

  Recomputes every batch's InwardQty/OutwardQty from the journal. The batch
  totals are a cache; this proves the cache is honest. Run it after a restore,
  after any bulk data fix, or as a monthly assurance job. If it changes a
  single row, something wrote stock without going through
  usp_PostStockTransaction and that is worth investigating.
==============================================================================*/
CREATE OR ALTER PROCEDURE usp_RebuildBatchQuantities
    @ItemId  INT = NULL,       -- NULL = every product
    @ReportOnly BIT = 0           -- 1 = show differences without writing
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ;WITH JournalTotals AS
    (
        SELECT b.BatchId,
               b.ItemId,
               b.InwardQty  AS CachedInward,
               b.OutwardQty AS CachedOutward,
               ISNULL(j.InQty,  0) AS JournalInward,
               ISNULL(j.OutQty, 0) AS JournalOutward
        FROM ItemBatches AS b
        OUTER APPLY
        (
            SELECT SUM(CASE WHEN st.Direction =  1 THEN st.Quantity ELSE 0 END) AS InQty,
                   SUM(CASE WHEN st.Direction = -1 THEN st.Quantity ELSE 0 END) AS OutQty
            FROM StockTransactions AS st
            WHERE st.BatchId = b.BatchId
        ) AS j
        WHERE (@ItemId IS NULL OR b.ItemId = @ItemId)
    )
    SELECT jt.BatchId, jt.ItemId, p.ItemName, b.BatchNumber,
           jt.CachedInward, jt.JournalInward,
           jt.CachedOutward, jt.JournalOutward,
           (jt.CachedInward - jt.CachedOutward)   AS CachedCurrentQty,
           (jt.JournalInward - jt.JournalOutward) AS JournalCurrentQty
    INTO #Differences
    FROM JournalTotals AS jt
    INNER JOIN ItemBatches AS b ON b.BatchId   = jt.BatchId
    INNER JOIN ItemMaster       AS p ON p.ItemId = jt.ItemId
    WHERE jt.CachedInward <> jt.JournalInward
       OR jt.CachedOutward <> jt.JournalOutward;

    SELECT * FROM #Differences ORDER BY ItemName, BatchNumber;

    IF @ReportOnly = 0
    BEGIN
        UPDATE b
           SET b.InwardQty  = d.JournalInward,
               b.OutwardQty = d.JournalOutward,
               b.UpdatedAt  = SYSUTCDATETIME()
        FROM ItemBatches AS b
        INNER JOIN #Differences AS d ON d.BatchId = b.BatchId;

        PRINT N'Batches corrected: ' + CAST(@@ROWCOUNT AS NVARCHAR(12));
    END

    DROP TABLE #Differences;
END
GO

/*==============================================================================
  usp_GetAvailableBatches

  FEFO - First Expiry, First Out. Correct for agri-inputs: a pesticide that
  expires in two months must leave before one that expires in two years,
  regardless of which arrived first. Plain FIFO would quietly age stock into
  a write-off.
==============================================================================*/
CREATE OR ALTER PROCEDURE usp_GetAvailableBatches
    @ItemId          INT,
    @LocationId         INT     = NULL,
    @RequiredQty        DECIMAL(18,3) = NULL,
    @ExcludeExpired     BIT     = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        b.BatchId,
        b.BatchNumber,
        b.LocationId,
        l.LocationName,
        b.ManufacturingDate,
        b.ExpiryDate,
        b.CurrentQty            AS AvailableQty,
        b.PurchaseRate,
        b.SellingRate,
        b.Mrp,
        DATEDIFF(DAY, CAST(GETDATE() AS DATE), b.ExpiryDate) AS DaysToExpiry,
        -- Running total lets the UI grey out batches beyond what is needed.
        SUM(b.CurrentQty) OVER (
            ORDER BY CASE WHEN b.ExpiryDate IS NULL THEN 1 ELSE 0 END,
                     b.ExpiryDate, b.BatchId
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) AS CumulativeQty,
        CASE WHEN @RequiredQty IS NULL THEN NULL
             ELSE @RequiredQty END AS RequiredQty
    FROM ItemBatches AS b
    INNER JOIN StorageLocations AS l ON l.LocationId = b.LocationId
    WHERE b.ItemId  = @ItemId
      AND b.CurrentQty > 0
      AND b.IsActive   = 1
      AND (@LocationId IS NULL OR b.LocationId = @LocationId)
      AND (@ExcludeExpired = 0 OR b.ExpiryDate IS NULL OR b.ExpiryDate >= CAST(GETDATE() AS DATE))
    -- NULL expiry sorts last: dated stock should always move first.
    ORDER BY CASE WHEN b.ExpiryDate IS NULL THEN 1 ELSE 0 END, b.ExpiryDate, b.BatchId;
END
GO

/*==============================================================================
  usp_DashboardSummary

  Returns the dashboard in six result sets rather than thirteen HTTP calls.
  Result sets, in order:
     1  headline figures (sales, purchase, stock value, dues, profit)
     2  stock alert counts
     3  recent bills
     4  top selling products for the period
     5  monthly sales + purchase series (graphs)
     6  category-wise stock split
==============================================================================*/
CREATE OR ALTER PROCEDURE usp_DashboardSummary
    @AsOnDate    DATE = NULL,
    @TopCount    INT  = 10,
    @GraphMonths INT  = 12
AS
BEGIN
    SET NOCOUNT ON;

    SET @AsOnDate = ISNULL(@AsOnDate, CAST(GETDATE() AS DATE));

    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@AsOnDate), MONTH(@AsOnDate), 1);
    DECLARE @MonthEnd   DATE = EOMONTH(@AsOnDate);
    DECLARE @GraphFrom  DATE = DATEADD(MONTH, -(@GraphMonths - 1), @MonthStart);

    /*---- 1. headline figures -------------------------------------------------*/
    SELECT
        @AsOnDate AS AsOnDate,

        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate = @AsOnDate)              AS TodaySales,
        (SELECT COUNT_BIG(*) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate = @AsOnDate)              AS TodayInvoiceCount,
        (SELECT ISNULL(SUM(GrossProfit), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate = @AsOnDate)              AS TodayProfit,

        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate BETWEEN @MonthStart AND @MonthEnd) AS MonthSales,
        (SELECT ISNULL(SUM(GrossProfit), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate BETWEEN @MonthStart AND @MonthEnd) AS MonthProfit,

        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Purchases
          WHERE Status = 'Posted' AND PurchaseDate = @AsOnDate)             AS TodayPurchase,
        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Purchases
          WHERE Status = 'Posted' AND PurchaseDate BETWEEN @MonthStart AND @MonthEnd) AS MonthPurchase,

        (SELECT ISNULL(SUM(CurrentQty * PurchaseRate), 0)
           FROM ItemBatches WHERE CurrentQty > 0)                    AS StockValueAtCost,
        (SELECT ISNULL(SUM(CurrentQty * Mrp), 0)
           FROM ItemBatches WHERE CurrentQty > 0)                    AS StockValueAtMrp,

        (SELECT ISNULL(SUM(BalanceAmount), 0) FROM Sales
          WHERE Status = 'Posted' AND BalanceAmount > 0)                    AS CustomerDue,
        (SELECT ISNULL(SUM(BalanceAmount), 0) FROM Purchases
          WHERE Status = 'Posted' AND BalanceAmount > 0)                    AS SupplierDue,

        (SELECT ISNULL(SUM(TotalAmount), 0) FROM Expenses
          WHERE Status = 'Posted' AND ExpenseDate BETWEEN @MonthStart AND @MonthEnd) AS MonthExpenses;

    /*---- 2. stock alerts ------------------------------------------------------*/
    SELECT
        (SELECT COUNT_BIG(*) FROM vw_ItemStock
          WHERE IsActive = 1 AND CurrentStock > 0 AND CurrentStock <= MinStockLevel) AS LowStockCount,
        (SELECT COUNT_BIG(*) FROM vw_ItemStock
          WHERE IsActive = 1 AND CurrentStock <= 0)                                  AS OutOfStockCount,
        (SELECT COUNT_BIG(*) FROM ItemBatches
          WHERE CurrentQty > 0 AND ExpiryDate IS NOT NULL
            AND ExpiryDate <  @AsOnDate)                                             AS ExpiredBatchCount,
        (SELECT COUNT_BIG(*) FROM ItemBatches
          WHERE CurrentQty > 0 AND ExpiryDate IS NOT NULL
            AND ExpiryDate >= @AsOnDate
            AND ExpiryDate <= DATEADD(DAY, 90, @AsOnDate))                           AS NearExpiryBatchCount,
        (SELECT ISNULL(SUM(CurrentQty * PurchaseRate), 0) FROM ItemBatches
          WHERE CurrentQty > 0 AND ExpiryDate IS NOT NULL
            AND ExpiryDate < @AsOnDate)                                              AS ExpiredStockValue,
        (SELECT COUNT_BIG(*) FROM ItemMaster WHERE IsDeleted = 0 AND IsActive = 1) AS ActiveItemCount;

    /*---- 3. recent bills ------------------------------------------------------*/
    SELECT TOP (@TopCount)
        s.SaleId,
        s.InvoiceNumber,
        s.InvoiceDate,
        ISNULL(c.CustomerName, ISNULL(s.WalkInCustomerName, N'Cash Customer')) AS CustomerName,
        ISNULL(c.Village, N'')  AS Village,
        s.SaleType,
        s.PaymentType,
        s.GrandTotal,
        s.ReceivedAmount,
        s.BalanceAmount,
        s.PaymentStatus
    FROM Sales AS s
    LEFT JOIN Customers AS c ON c.CustomerId = s.CustomerId
    WHERE s.Status = 'Posted'
    ORDER BY s.InvoiceDate DESC, s.SaleId DESC;

    /*---- 4. top selling products (current month) ------------------------------*/
    SELECT TOP (@TopCount)
        p.ItemId,
        p.ItemCode,
        p.ItemName,
        cat.ItemSubGroupName,
        ISNULL(co.CompanyName, N'') AS CompanyName,
        u.UnitCode,
        CAST(SUM(sd.TotalQuantity) AS DECIMAL(18,3)) AS QuantitySold,
        CAST(SUM(sd.TaxableAmount) AS DECIMAL(18,2)) AS SalesValue,
        CAST(SUM(sd.LineProfit)    AS DECIMAL(18,2)) AS Profit
    FROM SalesDetails AS sd
    INNER JOIN Sales      AS s   ON s.SaleId     = sd.SaleId
    INNER JOIN ItemMaster   AS p   ON p.ItemId  = sd.ItemId
    INNER JOIN ItemSubGroupMaster AS cat ON cat.ItemSubGroupId = p.ItemSubGroupId
    LEFT  JOIN Companies  AS co  ON co.CompanyId = p.CompanyId
    INNER JOIN Units      AS u   ON u.UnitId     = p.UnitId
    WHERE s.Status = 'Posted'
      AND s.InvoiceDate BETWEEN @MonthStart AND @MonthEnd
    GROUP BY p.ItemId, p.ItemCode, p.ItemName, cat.ItemSubGroupName, co.CompanyName, u.UnitCode
    ORDER BY SUM(sd.TaxableAmount) DESC;

    /*---- 5. monthly sales / purchase series -----------------------------------*/
    ;WITH Months AS
    (
        SELECT @GraphFrom AS MonthStart
        UNION ALL
        SELECT DATEADD(MONTH, 1, MonthStart)
        FROM Months
        WHERE DATEADD(MONTH, 1, MonthStart) <= @MonthStart
    )
    SELECT
        m.MonthStart,
        FORMAT(m.MonthStart, 'MMM yyyy')                        AS MonthLabel,
        ISNULL(sa.TotalSales,    0)                             AS SalesAmount,
        ISNULL(sa.GrossProfit,   0)                             AS ProfitAmount,
        ISNULL(pu.TotalPurchase, 0)                             AS PurchaseAmount,
        ISNULL(ex.TotalExpense,  0)                             AS ExpenseAmount
    FROM Months AS m
    OUTER APPLY
    (
        SELECT SUM(s.GrandTotal) AS TotalSales, SUM(s.GrossProfit) AS GrossProfit
        FROM Sales AS s
        WHERE s.Status = 'Posted'
          AND s.InvoiceDate >= m.MonthStart
          AND s.InvoiceDate <= EOMONTH(m.MonthStart)
    ) AS sa
    OUTER APPLY
    (
        SELECT SUM(p.GrandTotal) AS TotalPurchase
        FROM Purchases AS p
        WHERE p.Status = 'Posted'
          AND p.PurchaseDate >= m.MonthStart
          AND p.PurchaseDate <= EOMONTH(m.MonthStart)
    ) AS pu
    OUTER APPLY
    (
        SELECT SUM(e.TotalAmount) AS TotalExpense
        FROM Expenses AS e
        WHERE e.Status = 'Posted'
          AND e.ExpenseDate >= m.MonthStart
          AND e.ExpenseDate <= EOMONTH(m.MonthStart)
    ) AS ex
    ORDER BY m.MonthStart
    OPTION (MAXRECURSION 120);

    /*---- 6. category-wise stock ----------------------------------------------*/
    SELECT ItemSubGroupId, ItemSubGroupName, ItemCount, InStockCount, OutOfStockCount,
           LowStockCount, TotalQuantity, StockValueAtCost, StockValueAtMrp
    FROM vw_ItemSubGroupWiseStock
    WHERE ItemCount > 0
    ORDER BY StockValueAtCost DESC;
END
GO

PRINT N'11_StoredProcedures.sql completed.';
GO
