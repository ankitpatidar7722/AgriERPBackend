/*==============================================================================
  AgriERP  |  11_functions.sql   (PostgreSQL)
  ------------------------------------------------------------------------------
  PL/pgSQL parallels of the SQL Server stored procedures the application calls:
    fn_get_next_document_number  <- usp_GetNextDocumentNumber
    fn_post_stock_transaction    <- usp_PostStockTransaction
    fn_reverse_document_stock    <- usp_ReverseDocumentStock
    fn_dashboard_summary         <- usp_DashboardSummary (6 result sets via refcursor)

  Custom SQLSTATEs replace SQL Server THROW numbers so the C# services can map
  them identically on both providers:
    AG010 = no active number series   (was 50010)
    AG020 = quantity <= 0             (was 50020)
    AG021 = unknown transaction type  (was 50021)
    AG022 = batch not found           (was 50022)
    AG023 = batch/product mismatch    (was 50023)
    AG024 = insufficient stock        (was 50024)  <-- surfaced as a business rule

  usp_GetAvailableBatches and usp_RebuildBatchQuantities are intentionally not
  ported: nothing in the application invokes them (verified by grep).
==============================================================================*/

/*-------------------------- fn_get_next_document_number --------------------
  One UPDATE ... RETURNING does the whole read-increment-return. The matched
  NumberSeries row is locked for the statement, so concurrent Save clicks are
  serialised by the engine and receive consecutive numbers - never the same one. */
CREATE OR REPLACE FUNCTION fn_get_next_document_number(
    p_document_type    text,
    p_financial_year_id int DEFAULT NULL
) RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    v_fy            int := p_financial_year_id;
    v_current       int;
    v_prefix        text;
    v_suffix        text;
    v_separator     text;
    v_padding       int;
    v_include_year  boolean;
    v_series_fy     int;
    v_year_code     text;
BEGIN
    IF v_fy IS NULL THEN
        SELECT "FinancialYearId" INTO v_fy FROM "FinancialYears" WHERE "IsActive" = true;
    END IF;

    UPDATE "NumberSeries" ns
       SET "CurrentNumber" = ns."CurrentNumber" + 1,
           "UpdatedAt"     = (now() at time zone 'utc')
     WHERE ns."NumberSeriesId" = (
         SELECT inner_ns."NumberSeriesId"
         FROM "NumberSeries" inner_ns
         WHERE inner_ns."DocumentType" = p_document_type
           AND inner_ns."IsActive" = true
           AND (inner_ns."FinancialYearId" = v_fy OR inner_ns."FinancialYearId" IS NULL)
         ORDER BY CASE WHEN inner_ns."FinancialYearId" = v_fy THEN 0 ELSE 1 END,
                  inner_ns."NumberSeriesId"
         LIMIT 1
     )
    RETURNING ns."CurrentNumber", ns."Prefix", ns."Suffix", ns."Separator",
              ns."PaddingLength", ns."IncludeYearCode", ns."FinancialYearId"
      INTO v_current, v_prefix, v_suffix, v_separator, v_padding, v_include_year, v_series_fy;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No active number series is configured for document type ''%''.', p_document_type
            USING ERRCODE = 'AG010';
    END IF;

    SELECT "YearCode" INTO v_year_code
    FROM "FinancialYears"
    WHERE "FinancialYearId" = COALESCE(v_series_fy, v_fy);

    RETURN v_prefix
        || CASE WHEN v_include_year AND v_year_code IS NOT NULL
                THEN v_separator || v_year_code ELSE '' END
        || v_separator
        || right(repeat('0', v_padding) || v_current::text, v_padding)
        || v_suffix;
END $$;

/*-------------------------- fn_post_stock_transaction ----------------------
  The only supported way to move stock: validate, update the batch totals, and
  append the journal row - all in one statement's transaction. SELECT ... FOR
  UPDATE OF b takes the same row lock SQL Server's UPDLOCK did, so two sales of
  the last packet serialise and the second is correctly rejected. CurrentQty is
  a generated column, so updating Inward/OutwardQty maintains it automatically. */
CREATE OR REPLACE FUNCTION fn_post_stock_transaction(
    p_transaction_type_id smallint,
    p_transaction_date    timestamp,
    p_item_id             int,
    p_batch_id            bigint,
    p_location_id         int,
    p_quantity            numeric,
    p_rate                numeric DEFAULT 0,
    p_reference_type      text    DEFAULT NULL,
    p_reference_id        bigint  DEFAULT NULL,
    p_reference_detail_id bigint  DEFAULT NULL,
    p_reference_number    text    DEFAULT NULL,
    p_remarks             text    DEFAULT NULL,
    p_financial_year_id   int     DEFAULT NULL,
    p_user_id             int     DEFAULT NULL
) RETURNS bigint
LANGUAGE plpgsql
AS $$
DECLARE
    v_direction      smallint;
    v_available      numeric;
    v_allow_negative boolean;
    v_batch_item     int;
    v_item_name      text;
    v_new_id         bigint;
BEGIN
    IF p_quantity IS NULL OR p_quantity <= 0 THEN
        RAISE EXCEPTION 'Stock quantity must be greater than zero.' USING ERRCODE = 'AG020';
    END IF;

    SELECT "Direction" INTO v_direction
    FROM "TransactionTypes"
    WHERE "TransactionTypeId" = p_transaction_type_id AND "IsActive" = true;

    IF v_direction IS NULL THEN
        RAISE EXCEPTION 'Unknown or inactive stock transaction type.' USING ERRCODE = 'AG021';
    END IF;

    SELECT b."CurrentQty", b."ItemId", p."AllowNegativeStock", p."ItemName"
      INTO v_available, v_batch_item, v_allow_negative, v_item_name
      FROM "ItemBatches" b
      INNER JOIN "ItemMaster" p ON p."ItemId" = b."ItemId"
     WHERE b."BatchId" = p_batch_id
       FOR UPDATE OF b;

    IF v_batch_item IS NULL THEN
        RAISE EXCEPTION 'Batch not found.' USING ERRCODE = 'AG022';
    END IF;

    IF v_batch_item <> p_item_id THEN
        RAISE EXCEPTION 'The supplied batch does not belong to the supplied product.' USING ERRCODE = 'AG023';
    END IF;

    IF v_direction = -1 AND v_allow_negative = false AND v_available < p_quantity THEN
        RAISE EXCEPTION 'Insufficient stock for %. Available: %, required: %.',
            COALESCE(v_item_name, 'product'), v_available, p_quantity
            USING ERRCODE = 'AG024';
    END IF;

    UPDATE "ItemBatches"
       SET "InwardQty"  = "InwardQty"  + CASE WHEN v_direction =  1 THEN p_quantity ELSE 0 END,
           "OutwardQty" = "OutwardQty" + CASE WHEN v_direction = -1 THEN p_quantity ELSE 0 END,
           "UpdatedAt"  = (now() at time zone 'utc'),
           "UpdatedBy"  = p_user_id
     WHERE "BatchId" = p_batch_id;

    INSERT INTO "StockTransactions"
        ("TransactionDate","TransactionTypeId","ItemId","BatchId","LocationId",
         "Direction","Quantity","Rate",
         "ReferenceType","ReferenceId","ReferenceDetailId","ReferenceNumber",
         "Remarks","FinancialYearId","CreatedBy")
    VALUES
        (p_transaction_date, p_transaction_type_id, p_item_id, p_batch_id, p_location_id,
         v_direction, p_quantity, p_rate,
         p_reference_type, p_reference_id, p_reference_detail_id, p_reference_number,
         p_remarks, p_financial_year_id, p_user_id)
    RETURNING "StockTransactionId" INTO v_new_id;

    RETURN v_new_id;
END $$;

/*-------------------------- fn_reverse_document_stock ----------------------
  Cancelling a document writes an opposite journal row for each of its (not yet
  reversed) movements and puts the quantity back on each batch. Append-only, so
  the audit trail survives. Done as one data-modifying CTE: the `originals`
  snapshot is shared by the batch update and the reversal insert, and both see
  the pre-reversal state, so a double-cancel reverses nothing the second time. */
CREATE OR REPLACE FUNCTION fn_reverse_document_stock(
    p_reference_type text,
    p_reference_id   bigint,
    p_reversal_date  timestamp DEFAULT NULL,
    p_remarks        text      DEFAULT NULL,
    p_user_id        int       DEFAULT NULL
) RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    v_reversal_date timestamp := COALESCE(p_reversal_date, now() at time zone 'utc');
    v_remarks       text := COALESCE(p_remarks,
                        'Reversal of ' || p_reference_type || ' #' || p_reference_id::text);
    v_count         int;
BEGIN
    WITH originals AS (
        SELECT st."StockTransactionId", st."TransactionTypeId", st."ItemId", st."BatchId",
               st."LocationId", st."Direction", st."Quantity", st."Rate",
               st."ReferenceNumber", st."FinancialYearId"
        FROM "StockTransactions" st
        WHERE st."ReferenceType" = p_reference_type
          AND st."ReferenceId" = p_reference_id
          AND NOT EXISTS (SELECT 1 FROM "StockTransactions" rev
                          WHERE rev."ReversesTransactionId" = st."StockTransactionId")
    ),
    batch_agg AS (
        SELECT "BatchId",
               SUM(CASE WHEN "Direction" = -1 THEN "Quantity" ELSE 0 END) AS add_inward,
               SUM(CASE WHEN "Direction" =  1 THEN "Quantity" ELSE 0 END) AS add_outward
        FROM originals GROUP BY "BatchId"
    ),
    batch_upd AS (
        UPDATE "ItemBatches" b
           SET "InwardQty"  = b."InwardQty"  + a.add_inward,
               "OutwardQty" = b."OutwardQty" + a.add_outward,
               "UpdatedAt"  = (now() at time zone 'utc'),
               "UpdatedBy"  = p_user_id
        FROM batch_agg a
        WHERE b."BatchId" = a."BatchId"
        RETURNING 1
    ),
    ins AS (
        INSERT INTO "StockTransactions"
            ("TransactionDate","TransactionTypeId","ItemId","BatchId","LocationId",
             "Direction","Quantity","Rate",
             "ReferenceType","ReferenceId","ReferenceNumber",
             "ReversesTransactionId","Remarks","FinancialYearId","CreatedBy")
        SELECT v_reversal_date, o."TransactionTypeId", o."ItemId", o."BatchId", o."LocationId",
               o."Direction" * -1, o."Quantity", o."Rate",
               p_reference_type, p_reference_id, o."ReferenceNumber",
               o."StockTransactionId", v_remarks, o."FinancialYearId", p_user_id
        FROM originals o
        RETURNING 1
    )
    SELECT count(*) INTO v_count FROM ins;

    RETURN v_count;
END $$;

/*-------------------------- fn_dashboard_summary ---------------------------
  Returns the six dashboard blocks as six refcursors, in order. The caller opens
  a transaction, SELECTs this function (which returns the cursor names), then
  FETCH ALL from each. Same single round trip as usp_DashboardSummary; the
  recursive month/day series become generate_series, FORMAT becomes to_char. */
CREATE OR REPLACE FUNCTION fn_dashboard_summary(
    p_as_on_date    date DEFAULT NULL,
    p_top_count     int  DEFAULT 10,
    p_graph_months  int  DEFAULT 12,
    p_from_date     date DEFAULT NULL,
    p_to_date       date DEFAULT NULL
) RETURNS SETOF refcursor
LANGUAGE plpgsql
AS $$
DECLARE
    v_as_on       date := COALESCE(p_as_on_date, CURRENT_DATE);
    v_month_start date;
    v_month_end   date;
    v_graph_from  date;
    v_has_range   boolean;
    v_range_from  date;
    v_range_to    date;
    v_span_days   int;
    v_tmp         date;
    c_headline refcursor := 'dash_headline';
    c_alerts   refcursor := 'dash_alerts';
    c_bills    refcursor := 'dash_bills';
    c_items    refcursor := 'dash_items';
    c_trend    refcursor := 'dash_trend';
    c_category refcursor := 'dash_category';
BEGIN
    v_month_start := date_trunc('month', v_as_on)::date;
    v_month_end   := (v_month_start + interval '1 month - 1 day')::date;
    v_graph_from  := (v_month_start - ((p_graph_months - 1) || ' months')::interval)::date;
    v_has_range   := (p_from_date IS NOT NULL OR p_to_date IS NOT NULL);
    v_range_from  := COALESCE(p_from_date, v_month_start);
    v_range_to    := COALESCE(p_to_date,   v_month_end);
    IF v_range_to < v_range_from THEN
        v_tmp := v_range_from; v_range_from := v_range_to; v_range_to := v_tmp;
    END IF;
    v_span_days := (v_range_to - v_range_from);

    /* 1. headline */
    OPEN c_headline FOR
    SELECT
        v_as_on AS "AsOnDate",
        (SELECT COALESCE(SUM("GrandTotal"),0) FROM "Sales" WHERE "Status"='Posted' AND "InvoiceDate"=v_as_on) AS "TodaySales",
        (SELECT count(*) FROM "Sales" WHERE "Status"='Posted' AND "InvoiceDate"=v_as_on) AS "TodayInvoiceCount",
        (SELECT COALESCE(SUM("GrossProfit"),0) FROM "Sales" WHERE "Status"='Posted' AND "InvoiceDate"=v_as_on) AS "TodayProfit",
        (SELECT COALESCE(SUM("GrandTotal"),0) FROM "Sales" WHERE "Status"='Posted' AND "InvoiceDate" BETWEEN v_range_from AND v_range_to) AS "MonthSales",
        (SELECT COALESCE(SUM("GrossProfit"),0) FROM "Sales" WHERE "Status"='Posted' AND "InvoiceDate" BETWEEN v_range_from AND v_range_to) AS "MonthProfit",
        (SELECT COALESCE(SUM("GrandTotal"),0) FROM "Purchases" WHERE "Status"='Posted' AND "PurchaseDate"=v_as_on) AS "TodayPurchase",
        (SELECT COALESCE(SUM("GrandTotal"),0) FROM "Purchases" WHERE "Status"='Posted' AND "PurchaseDate" BETWEEN v_range_from AND v_range_to) AS "MonthPurchase",
        (SELECT COALESCE(SUM("CurrentQty"*"PurchaseRate"),0) FROM "ItemBatches" WHERE "CurrentQty">0) AS "StockValueAtCost",
        (SELECT COALESCE(SUM("CurrentQty"*"Mrp"),0) FROM "ItemBatches" WHERE "CurrentQty">0) AS "StockValueAtMrp",
        (SELECT COALESCE(SUM("BalanceAmount"),0) FROM "Sales" WHERE "Status"='Posted' AND "BalanceAmount">0) AS "CustomerDue",
        (SELECT COALESCE(SUM("BalanceAmount"),0) FROM "Purchases" WHERE "Status"='Posted' AND "BalanceAmount">0) AS "SupplierDue",
        (SELECT COALESCE(SUM("TotalAmount"),0) FROM "Expenses" WHERE "Status"='Posted' AND "ExpenseDate" BETWEEN v_range_from AND v_range_to) AS "MonthExpenses";
    RETURN NEXT c_headline;

    /* 2. alerts */
    OPEN c_alerts FOR
    SELECT
        (SELECT count(*) FROM "vw_ItemStock" WHERE "IsActive"=true AND "CurrentStock">0 AND "CurrentStock"<="MinStockLevel") AS "LowStockCount",
        (SELECT count(*) FROM "vw_ItemStock" WHERE "IsActive"=true AND "CurrentStock"<=0) AS "OutOfStockCount",
        (SELECT count(*) FROM "ItemBatches" WHERE "CurrentQty">0 AND "ExpiryDate" IS NOT NULL AND "ExpiryDate"<v_as_on) AS "ExpiredBatchCount",
        (SELECT count(*) FROM "ItemBatches" WHERE "CurrentQty">0 AND "ExpiryDate" IS NOT NULL AND "ExpiryDate">=v_as_on AND "ExpiryDate"<=v_as_on+90) AS "NearExpiryBatchCount",
        (SELECT COALESCE(SUM("CurrentQty"*"PurchaseRate"),0) FROM "ItemBatches" WHERE "CurrentQty">0 AND "ExpiryDate" IS NOT NULL AND "ExpiryDate"<v_as_on) AS "ExpiredStockValue",
        (SELECT count(*) FROM "ItemMaster" WHERE "IsDeleted"=false AND "IsActive"=true) AS "ActiveItemCount";
    RETURN NEXT c_alerts;

    /* 3. recent bills */
    OPEN c_bills FOR
    SELECT s."SaleId", s."InvoiceNumber", s."InvoiceDate",
        COALESCE(c."CustomerName", COALESCE(s."WalkInCustomerName", 'Cash Customer')) AS "CustomerName",
        COALESCE(c."Village", '') AS "Village",
        s."SaleType", s."PaymentType", s."GrandTotal", s."ReceivedAmount", s."BalanceAmount", s."PaymentStatus"
    FROM "Sales" s
    LEFT JOIN "Customers" c ON c."CustomerId" = s."CustomerId"
    WHERE s."Status"='Posted'
    ORDER BY s."InvoiceDate" DESC, s."SaleId" DESC
    LIMIT p_top_count;
    RETURN NEXT c_bills;

    /* 4. top selling items */
    OPEN c_items FOR
    SELECT p."ItemId", p."ItemCode", p."ItemName", cat."ItemSubGroupName",
        COALESCE(co."CompanyName", '') AS "CompanyName", u."UnitCode",
        CAST(SUM(sd."TotalQuantity") AS numeric(18,3)) AS "QuantitySold",
        CAST(SUM(sd."TaxableAmount") AS numeric(18,2)) AS "SalesValue",
        CAST(SUM(sd."LineProfit")    AS numeric(18,2)) AS "Profit"
    FROM "SalesDetails" sd
    INNER JOIN "Sales" s               ON s."SaleId" = sd."SaleId"
    INNER JOIN "ItemMaster" p          ON p."ItemId" = sd."ItemId"
    INNER JOIN "ItemSubGroupMaster" cat ON cat."ItemSubGroupId" = p."ItemSubGroupId"
    LEFT  JOIN "Companies" co          ON co."CompanyId" = p."CompanyId"
    INNER JOIN "Units" u               ON u."UnitId" = p."UnitId"
    WHERE s."Status"='Posted' AND s."InvoiceDate" BETWEEN v_range_from AND v_range_to
    GROUP BY p."ItemId", p."ItemCode", p."ItemName", cat."ItemSubGroupName", co."CompanyName", u."UnitCode"
    ORDER BY SUM(sd."TaxableAmount") DESC
    LIMIT p_top_count;
    RETURN NEXT c_items;

    /* 5. trend series - monthly (no range), daily (<=62d), or monthly (>62d) */
    IF v_has_range = false THEN
        OPEN c_trend FOR
        SELECT m."MonthStart",
            to_char(m."MonthStart", 'Mon YYYY')   AS "MonthLabel",
            COALESCE(sa."TotalSales",0)            AS "SalesAmount",
            COALESCE(sa."GrossProfit",0)           AS "ProfitAmount",
            COALESCE(pu."TotalPurchase",0)         AS "PurchaseAmount",
            COALESCE(ex."TotalExpense",0)          AS "ExpenseAmount"
        FROM (SELECT generate_series(v_graph_from::timestamp, v_month_start::timestamp, interval '1 month')::date AS "MonthStart") m
        LEFT JOIN LATERAL (SELECT SUM(s."GrandTotal") AS "TotalSales", SUM(s."GrossProfit") AS "GrossProfit"
                           FROM "Sales" s WHERE s."Status"='Posted'
                             AND s."InvoiceDate">=m."MonthStart" AND s."InvoiceDate"<=(m."MonthStart" + interval '1 month - 1 day')::date) sa ON true
        LEFT JOIN LATERAL (SELECT SUM(p."GrandTotal") AS "TotalPurchase"
                           FROM "Purchases" p WHERE p."Status"='Posted'
                             AND p."PurchaseDate">=m."MonthStart" AND p."PurchaseDate"<=(m."MonthStart" + interval '1 month - 1 day')::date) pu ON true
        LEFT JOIN LATERAL (SELECT SUM(e."TotalAmount") AS "TotalExpense"
                           FROM "Expenses" e WHERE e."Status"='Posted'
                             AND e."ExpenseDate">=m."MonthStart" AND e."ExpenseDate"<=(m."MonthStart" + interval '1 month - 1 day')::date) ex ON true
        ORDER BY m."MonthStart";
    ELSIF v_span_days <= 62 THEN
        OPEN c_trend FOR
        SELECT d."MonthStart",
            to_char(d."MonthStart", 'DD Mon')      AS "MonthLabel",
            COALESCE(sa."TotalSales",0)            AS "SalesAmount",
            COALESCE(sa."GrossProfit",0)           AS "ProfitAmount",
            COALESCE(pu."TotalPurchase",0)         AS "PurchaseAmount",
            COALESCE(ex."TotalExpense",0)          AS "ExpenseAmount"
        FROM (SELECT generate_series(v_range_from::timestamp, v_range_to::timestamp, interval '1 day')::date AS "MonthStart") d
        LEFT JOIN LATERAL (SELECT SUM(s."GrandTotal") AS "TotalSales", SUM(s."GrossProfit") AS "GrossProfit"
                           FROM "Sales" s WHERE s."Status"='Posted' AND s."InvoiceDate"=d."MonthStart") sa ON true
        LEFT JOIN LATERAL (SELECT SUM(p."GrandTotal") AS "TotalPurchase"
                           FROM "Purchases" p WHERE p."Status"='Posted' AND p."PurchaseDate"=d."MonthStart") pu ON true
        LEFT JOIN LATERAL (SELECT SUM(e."TotalAmount") AS "TotalExpense"
                           FROM "Expenses" e WHERE e."Status"='Posted' AND e."ExpenseDate"=d."MonthStart") ex ON true
        ORDER BY d."MonthStart";
    ELSE
        OPEN c_trend FOR
        SELECT m."MonthStart",
            to_char(m."MonthStart", 'Mon YYYY')   AS "MonthLabel",
            COALESCE(sa."TotalSales",0)            AS "SalesAmount",
            COALESCE(sa."GrossProfit",0)           AS "ProfitAmount",
            COALESCE(pu."TotalPurchase",0)         AS "PurchaseAmount",
            COALESCE(ex."TotalExpense",0)          AS "ExpenseAmount"
        FROM (SELECT generate_series(date_trunc('month', v_range_from)::timestamp,
                                     date_trunc('month', v_range_to)::timestamp, interval '1 month')::date AS "MonthStart") m
        LEFT JOIN LATERAL (SELECT SUM(s."GrandTotal") AS "TotalSales", SUM(s."GrossProfit") AS "GrossProfit"
                           FROM "Sales" s WHERE s."Status"='Posted'
                             AND s."InvoiceDate">=m."MonthStart" AND s."InvoiceDate"<=(m."MonthStart" + interval '1 month - 1 day')::date) sa ON true
        LEFT JOIN LATERAL (SELECT SUM(p."GrandTotal") AS "TotalPurchase"
                           FROM "Purchases" p WHERE p."Status"='Posted'
                             AND p."PurchaseDate">=m."MonthStart" AND p."PurchaseDate"<=(m."MonthStart" + interval '1 month - 1 day')::date) pu ON true
        LEFT JOIN LATERAL (SELECT SUM(e."TotalAmount") AS "TotalExpense"
                           FROM "Expenses" e WHERE e."Status"='Posted'
                             AND e."ExpenseDate">=m."MonthStart" AND e."ExpenseDate"<=(m."MonthStart" + interval '1 month - 1 day')::date) ex ON true
        ORDER BY m."MonthStart";
    END IF;
    RETURN NEXT c_trend;

    /* 6. category-wise stock */
    OPEN c_category FOR
    SELECT "ItemSubGroupId","ItemSubGroupName","ItemCount","InStockCount","OutOfStockCount",
           "LowStockCount","TotalQuantity","StockValueAtCost","StockValueAtMrp"
    FROM "vw_ItemSubGroupWiseStock"
    WHERE "ItemCount" > 0
    ORDER BY "StockValueAtCost" DESC;
    RETURN NEXT c_category;
END $$;
