/*==============================================================================
  34_DashboardDateRange.sql

  Makes usp_DashboardSummary date-range aware so the Sales/Purchase dashboards
  can be filtered by an arbitrary period (Today, Last 7 Days, This Quarter, a
  specific year, ...).

  Two new optional parameters, @FromDate / @ToDate, define a reporting window:

    * When BOTH are NULL the window is the calendar month of @AsOnDate, so every
      figure keeps EXACTLY its previous meaning (fully backward compatible).
    * When a range is passed:
        - the "month" period figures (MonthSales/MonthProfit/MonthPurchase/
          MonthExpenses) and the top-selling-items list are computed over the
          window instead of the calendar month;
        - the trend series (result set 5) is bucketed by the window's span:
          <= 62 days  -> one point per DAY  (Day/Week/single-month views)
          >  62 days  -> one point per MONTH (Quarter/Year views).

  Snapshot figures stay as-of-now on purpose and ignore the window entirely:
  StockValueAtCost/Mrp, CustomerDue, SupplierDue and the stock-alert counts are
  "current state", not "activity in a period". The "today" figures likewise stay
  anchored to @AsOnDate (a live "as of now" stat shown beside the period total).

  QUOTED_IDENTIFIER/ANSI_NULLS are set ON before the module so the settings are
  captured correctly at create time (matches the rest of the schema scripts).
==============================================================================*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE usp_DashboardSummary
    @AsOnDate    DATE = NULL,
    @TopCount    INT  = 10,
    @GraphMonths INT  = 12,
    @FromDate    DATE = NULL,
    @ToDate      DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @AsOnDate = ISNULL(@AsOnDate, CAST(GETDATE() AS DATE));

    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@AsOnDate), MONTH(@AsOnDate), 1);
    DECLARE @MonthEnd   DATE = EOMONTH(@AsOnDate);
    DECLARE @GraphFrom  DATE = DATEADD(MONTH, -(@GraphMonths - 1), @MonthStart);

    -- The reporting window. No range -> the calendar month of @AsOnDate, which
    -- keeps every period figure identical to the pre-range behaviour.
    DECLARE @HasRange  BIT  = CASE WHEN @FromDate IS NOT NULL OR @ToDate IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @RangeFrom DATE = ISNULL(@FromDate, @MonthStart);
    DECLARE @RangeTo   DATE = ISNULL(@ToDate,   @MonthEnd);

    -- Tolerate a swapped from/to rather than returning an empty window.
    IF @RangeTo < @RangeFrom
    BEGIN
        DECLARE @Swap DATE = @RangeFrom;
        SET @RangeFrom = @RangeTo;
        SET @RangeTo   = @Swap;
    END

    DECLARE @SpanDays INT = DATEDIFF(DAY, @RangeFrom, @RangeTo);

    /*---- 1. headline figures -------------------------------------------------*/
    SELECT
        @AsOnDate AS AsOnDate,

        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate = @AsOnDate)              AS TodaySales,
        (SELECT COUNT_BIG(*) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate = @AsOnDate)              AS TodayInvoiceCount,
        (SELECT ISNULL(SUM(GrossProfit), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate = @AsOnDate)             AS TodayProfit,

        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate BETWEEN @RangeFrom AND @RangeTo) AS MonthSales,
        (SELECT ISNULL(SUM(GrossProfit), 0) FROM Sales
          WHERE Status = 'Posted' AND InvoiceDate BETWEEN @RangeFrom AND @RangeTo) AS MonthProfit,

        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Purchases
          WHERE Status = 'Posted' AND PurchaseDate = @AsOnDate)             AS TodayPurchase,
        (SELECT ISNULL(SUM(GrandTotal), 0) FROM Purchases
          WHERE Status = 'Posted' AND PurchaseDate BETWEEN @RangeFrom AND @RangeTo) AS MonthPurchase,

        (SELECT ISNULL(SUM(CurrentQty * PurchaseRate), 0)
           FROM ItemBatches WHERE CurrentQty > 0)                    AS StockValueAtCost,
        (SELECT ISNULL(SUM(CurrentQty * Mrp), 0)
           FROM ItemBatches WHERE CurrentQty > 0)                    AS StockValueAtMrp,

        (SELECT ISNULL(SUM(BalanceAmount), 0) FROM Sales
          WHERE Status = 'Posted' AND BalanceAmount > 0)                    AS CustomerDue,
        (SELECT ISNULL(SUM(BalanceAmount), 0) FROM Purchases
          WHERE Status = 'Posted' AND BalanceAmount > 0)                    AS SupplierDue,

        (SELECT ISNULL(SUM(TotalAmount), 0) FROM Expenses
          WHERE Status = 'Posted' AND ExpenseDate BETWEEN @RangeFrom AND @RangeTo) AS MonthExpenses;

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

    /*---- 4. top selling products (over the reporting window) ------------------*/
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
      AND s.InvoiceDate BETWEEN @RangeFrom AND @RangeTo
    GROUP BY p.ItemId, p.ItemCode, p.ItemName, cat.ItemSubGroupName, co.CompanyName, u.UnitCode
    ORDER BY SUM(sd.TaxableAmount) DESC;

    /*---- 5. trend series ------------------------------------------------------
      No range  -> last @GraphMonths calendar months (original behaviour).
      Range, span <= 62 days -> one point per day across the window.
      Range, span >  62 days -> one point per month across the window.
    --------------------------------------------------------------------------*/
    IF @HasRange = 0
    BEGIN
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
            FORMAT(m.MonthStart, 'MMM yyyy')                       AS MonthLabel,
            ISNULL(sa.TotalSales,    0)                            AS SalesAmount,
            ISNULL(sa.GrossProfit,   0)                            AS ProfitAmount,
            ISNULL(pu.TotalPurchase, 0)                            AS PurchaseAmount,
            ISNULL(ex.TotalExpense,  0)                            AS ExpenseAmount
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
    END
    ELSE IF @SpanDays <= 62
    BEGIN
        ;WITH Days AS
        (
            SELECT @RangeFrom AS D
            UNION ALL
            SELECT DATEADD(DAY, 1, D)
            FROM Days
            WHERE DATEADD(DAY, 1, D) <= @RangeTo
        )
        SELECT
            d.D                                                    AS MonthStart,
            FORMAT(d.D, 'dd MMM')                                  AS MonthLabel,
            ISNULL(sa.TotalSales,    0)                            AS SalesAmount,
            ISNULL(sa.GrossProfit,   0)                            AS ProfitAmount,
            ISNULL(pu.TotalPurchase, 0)                            AS PurchaseAmount,
            ISNULL(ex.TotalExpense,  0)                            AS ExpenseAmount
        FROM Days AS d
        OUTER APPLY
        (
            SELECT SUM(s.GrandTotal) AS TotalSales, SUM(s.GrossProfit) AS GrossProfit
            FROM Sales AS s
            WHERE s.Status = 'Posted' AND s.InvoiceDate = d.D
        ) AS sa
        OUTER APPLY
        (
            SELECT SUM(p.GrandTotal) AS TotalPurchase
            FROM Purchases AS p
            WHERE p.Status = 'Posted' AND p.PurchaseDate = d.D
        ) AS pu
        OUTER APPLY
        (
            SELECT SUM(e.TotalAmount) AS TotalExpense
            FROM Expenses AS e
            WHERE e.Status = 'Posted' AND e.ExpenseDate = d.D
        ) AS ex
        ORDER BY d.D
        OPTION (MAXRECURSION 200);
    END
    ELSE
    BEGIN
        DECLARE @MFrom DATE = DATEFROMPARTS(YEAR(@RangeFrom), MONTH(@RangeFrom), 1);
        DECLARE @MTo   DATE = DATEFROMPARTS(YEAR(@RangeTo),   MONTH(@RangeTo),   1);

        ;WITH Months AS
        (
            SELECT @MFrom AS MonthStart
            UNION ALL
            SELECT DATEADD(MONTH, 1, MonthStart)
            FROM Months
            WHERE DATEADD(MONTH, 1, MonthStart) <= @MTo
        )
        SELECT
            m.MonthStart,
            FORMAT(m.MonthStart, 'MMM yyyy')                       AS MonthLabel,
            ISNULL(sa.TotalSales,    0)                            AS SalesAmount,
            ISNULL(sa.GrossProfit,   0)                            AS ProfitAmount,
            ISNULL(pu.TotalPurchase, 0)                            AS PurchaseAmount,
            ISNULL(ex.TotalExpense,  0)                            AS ExpenseAmount
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
    END

    /*---- 6. category-wise stock ----------------------------------------------*/
    SELECT ItemSubGroupId, ItemSubGroupName, ItemCount, InStockCount, OutOfStockCount,
           LowStockCount, TotalQuantity, StockValueAtCost, StockValueAtMrp
    FROM vw_ItemSubGroupWiseStock
    WHERE ItemCount > 0
    ORDER BY StockValueAtCost DESC;
END
GO

PRINT N'34_DashboardDateRange.sql completed.';
GO
