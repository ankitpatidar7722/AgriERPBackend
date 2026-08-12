using AgriERP.Application.Features.Dashboard;
using AgriERP.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
// GetDbTransaction() is an extension on IDbContextTransaction declared here.
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace AgriERP.Persistence.Services;

/// <summary>
/// Reads the six dashboard blocks in one round trip on both providers:
///   SQL Server  -> usp_DashboardSummary returns six result sets from one exec.
///   PostgreSQL  -> fn_dashboard_summary opens six refcursors; we FETCH each.
///
/// The row-parsing is identical - the function aliases every column exactly as
/// the procedure names it - so only the plumbing that produces the six readers
/// differs by provider.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly AgriErpDbContext _context;

    public DashboardService(AgriErpDbContext context) => _context = context;

    public Task<DashboardDto> GetAsync(
        DateTime? asOnDate, int topCount, int graphMonths,
        DateTime? fromDate = null, DateTime? toDate = null,
        CancellationToken ct = default)
        => _context.Database.IsNpgsql()
            ? GetNpgsqlAsync(asOnDate, topCount, graphMonths, fromDate, toDate, ct)
            : GetSqlServerAsync(asOnDate, topCount, graphMonths, fromDate, toDate, ct);

    /*----------------------------- SQL Server ------------------------------*/
    private async Task<DashboardDto> GetSqlServerAsync(
        DateTime? asOnDate, int topCount, int graphMonths,
        DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var dashboard = new DashboardDto();
        var connection = _context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "usp_DashboardSummary";
            command.CommandType = CommandType.StoredProcedure;
            if (_context.Database.CurrentTransaction is { } transaction)
                command.Transaction = transaction.GetDbTransaction();

            command.Parameters.Add(new SqlParameter("@AsOnDate", SqlDbType.Date) { Value = (object?)asOnDate?.Date ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@TopCount", SqlDbType.Int) { Value = topCount });
            command.Parameters.Add(new SqlParameter("@GraphMonths", SqlDbType.Int) { Value = graphMonths });
            command.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = (object?)fromDate?.Date ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = (object?)toDate?.Date ?? DBNull.Value });

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct)) dashboard.Headline = ParseHeadline(reader);
            if (await reader.NextResultAsync(ct) && await reader.ReadAsync(ct)) dashboard.Alerts = ParseAlerts(reader);
            if (await reader.NextResultAsync(ct)) dashboard.RecentBills = await ReadAll(reader, ParseRecentBill, ct);
            if (await reader.NextResultAsync(ct)) dashboard.TopItems = await ReadAll(reader, ParseTopItem, ct);
            if (await reader.NextResultAsync(ct)) dashboard.MonthlyTrend = await ReadAll(reader, ParseMonthPoint, ct);
            if (await reader.NextResultAsync(ct)) dashboard.ItemSubGroupStock = await ReadAll(reader, ParseItemSubGroupStock, ct);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
        return dashboard;
    }

    /*----------------------------- PostgreSQL ------------------------------*/
    private async Task<DashboardDto> GetNpgsqlAsync(
        DateTime? asOnDate, int topCount, int graphMonths,
        DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var dashboard = new DashboardDto();
        var connection = _context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed) await connection.OpenAsync(ct);

        // Refcursors are only valid inside a transaction. Reuse the caller's if
        // one is open, otherwise run in a short one of our own.
        var ownTransaction = _context.Database.CurrentTransaction is null;
        var transaction = ownTransaction
            ? await _context.Database.BeginTransactionAsync(ct)
            : _context.Database.CurrentTransaction!;
        try
        {
            await using (var call = connection.CreateCommand())
            {
                call.Transaction = transaction.GetDbTransaction();
                call.CommandText =
                    "SELECT fn_dashboard_summary(@AsOnDate::date, @TopCount::int, @GraphMonths::int, @FromDate::date, @ToDate::date)";
                AddParam(call, "AsOnDate", (object?)asOnDate?.Date ?? DBNull.Value);
                AddParam(call, "TopCount", topCount);
                AddParam(call, "GraphMonths", graphMonths);
                AddParam(call, "FromDate", (object?)fromDate?.Date ?? DBNull.Value);
                AddParam(call, "ToDate", (object?)toDate?.Date ?? DBNull.Value);
                await call.ExecuteNonQueryAsync(ct);   // opens the six cursors
            }

            dashboard.Headline          = await FetchOne(connection, transaction, "dash_headline", ParseHeadline, ct) ?? new DashboardHeadlineDto();
            dashboard.Alerts            = await FetchOne(connection, transaction, "dash_alerts",   ParseAlerts,   ct) ?? new DashboardAlertsDto();
            dashboard.RecentBills       = await FetchAll(connection, transaction, "dash_bills",    ParseRecentBill, ct);
            dashboard.TopItems          = await FetchAll(connection, transaction, "dash_items",    ParseTopItem, ct);
            dashboard.MonthlyTrend      = await FetchAll(connection, transaction, "dash_trend",    ParseMonthPoint, ct);
            dashboard.ItemSubGroupStock = await FetchAll(connection, transaction, "dash_category", ParseItemSubGroupStock, ct);

            if (ownTransaction) await transaction.CommitAsync(ct);
        }
        finally
        {
            if (ownTransaction) await transaction.DisposeAsync();
            if (wasClosed) await connection.CloseAsync();
        }
        return dashboard;
    }

    /*----------------------------- row parsers -----------------------------*/
    private static DashboardHeadlineDto ParseHeadline(IDataRecord r) => new()
    {
        AsOnDate          = r.GetDateTime(r.GetOrdinal("AsOnDate")),
        TodaySales        = GetDecimal(r, "TodaySales"),
        TodayInvoiceCount = GetInt64(r, "TodayInvoiceCount"),
        TodayProfit       = GetDecimal(r, "TodayProfit"),
        MonthSales        = GetDecimal(r, "MonthSales"),
        MonthProfit       = GetDecimal(r, "MonthProfit"),
        TodayPurchase     = GetDecimal(r, "TodayPurchase"),
        MonthPurchase     = GetDecimal(r, "MonthPurchase"),
        StockValueAtCost  = GetDecimal(r, "StockValueAtCost"),
        StockValueAtMrp   = GetDecimal(r, "StockValueAtMrp"),
        CustomerDue       = GetDecimal(r, "CustomerDue"),
        SupplierDue       = GetDecimal(r, "SupplierDue"),
        MonthExpenses     = GetDecimal(r, "MonthExpenses")
    };

    private static DashboardAlertsDto ParseAlerts(IDataRecord r) => new()
    {
        LowStockCount        = GetInt64(r, "LowStockCount"),
        OutOfStockCount      = GetInt64(r, "OutOfStockCount"),
        ExpiredBatchCount    = GetInt64(r, "ExpiredBatchCount"),
        NearExpiryBatchCount = GetInt64(r, "NearExpiryBatchCount"),
        ExpiredStockValue    = GetDecimal(r, "ExpiredStockValue"),
        ActiveItemCount      = GetInt64(r, "ActiveItemCount")
    };

    private static DashboardRecentBillDto ParseRecentBill(IDataRecord r) => new()
    {
        SaleId         = GetInt64(r, "SaleId"),
        InvoiceNumber  = GetString(r, "InvoiceNumber"),
        InvoiceDate    = r.GetDateTime(r.GetOrdinal("InvoiceDate")),
        CustomerName   = GetString(r, "CustomerName"),
        Village        = GetString(r, "Village"),
        SaleType       = GetString(r, "SaleType"),
        PaymentType    = GetString(r, "PaymentType"),
        GrandTotal     = GetDecimal(r, "GrandTotal"),
        ReceivedAmount = GetDecimal(r, "ReceivedAmount"),
        BalanceAmount  = GetDecimal(r, "BalanceAmount"),
        PaymentStatus  = GetString(r, "PaymentStatus")
    };

    private static DashboardTopItemDto ParseTopItem(IDataRecord r) => new()
    {
        ItemId           = GetInt32(r, "ItemId"),
        ItemCode         = GetString(r, "ItemCode"),
        ItemName         = GetString(r, "ItemName"),
        ItemSubGroupName = GetString(r, "ItemSubGroupName"),
        CompanyName      = GetString(r, "CompanyName"),
        UnitCode         = GetString(r, "UnitCode"),
        QuantitySold     = GetDecimal(r, "QuantitySold"),
        SalesValue       = GetDecimal(r, "SalesValue"),
        Profit           = GetDecimal(r, "Profit")
    };

    private static DashboardMonthPointDto ParseMonthPoint(IDataRecord r) => new()
    {
        MonthStart     = r.GetDateTime(r.GetOrdinal("MonthStart")),
        MonthLabel     = GetString(r, "MonthLabel"),
        SalesAmount    = GetDecimal(r, "SalesAmount"),
        ProfitAmount   = GetDecimal(r, "ProfitAmount"),
        PurchaseAmount = GetDecimal(r, "PurchaseAmount"),
        ExpenseAmount  = GetDecimal(r, "ExpenseAmount")
    };

    private static DashboardItemSubGroupStockDto ParseItemSubGroupStock(IDataRecord r) => new()
    {
        ItemSubGroupId   = GetInt32(r, "ItemSubGroupId"),
        ItemSubGroupName = GetString(r, "ItemSubGroupName"),
        ItemCount        = GetInt64(r, "ItemCount"),
        InStockCount     = GetInt32(r, "InStockCount"),
        OutOfStockCount  = GetInt32(r, "OutOfStockCount"),
        LowStockCount    = GetInt32(r, "LowStockCount"),
        TotalQuantity    = GetDecimal(r, "TotalQuantity"),
        StockValueAtCost = GetDecimal(r, "StockValueAtCost"),
        StockValueAtMrp  = GetDecimal(r, "StockValueAtMrp")
    };

    /*----------------------------- plumbing --------------------------------*/
    private static async Task<List<T>> ReadAll<T>(DbDataReader reader, Func<IDataRecord, T> parse, CancellationToken ct)
    {
        var list = new List<T>();
        while (await reader.ReadAsync(ct)) list.Add(parse(reader));
        return list;
    }

    private async Task<T?> FetchOne<T>(
        DbConnection connection, IDbContextTransaction transaction, string cursor,
        Func<IDataRecord, T> parse, CancellationToken ct) where T : class
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "FETCH ALL IN " + cursor;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? parse(reader) : null;
    }

    private async Task<List<T>> FetchAll<T>(
        DbConnection connection, IDbContextTransaction transaction, string cursor,
        Func<IDataRecord, T> parse, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "FETCH ALL IN " + cursor;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await ReadAll(reader, parse, ct);
    }

    private static void AddParam(DbCommand command, string name, object? value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        command.Parameters.Add(p);
    }

    // SUM() over no rows returns NULL, and several tiles legitimately have no
    // rows on a quiet day. These readers turn that into 0/empty rather than
    // letting an empty shop crash its own dashboard.
    private static decimal GetDecimal(IDataRecord record, string name)
    {
        var ordinal = record.GetOrdinal(name);
        return record.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(record.GetValue(ordinal));
    }

    private static long GetInt64(IDataRecord record, string name)
    {
        var ordinal = record.GetOrdinal(name);
        return record.IsDBNull(ordinal) ? 0L : Convert.ToInt64(record.GetValue(ordinal));
    }

    private static int GetInt32(IDataRecord record, string name)
    {
        var ordinal = record.GetOrdinal(name);
        return record.IsDBNull(ordinal) ? 0 : Convert.ToInt32(record.GetValue(ordinal));
    }

    private static string GetString(IDataRecord record, string name)
    {
        var ordinal = record.GetOrdinal(name);
        return record.IsDBNull(ordinal) ? string.Empty : record.GetString(ordinal);
    }
}
