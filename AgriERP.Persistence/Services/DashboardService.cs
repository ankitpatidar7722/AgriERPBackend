using AgriERP.Application.Features.Dashboard;
using AgriERP.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
// GetDbTransaction() is an extension on IDbContextTransaction declared here.
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace AgriERP.Persistence.Services;

/// <summary>
/// Reads usp_DashboardSummary.
///
/// Raw ADO rather than EF: the procedure returns six differently-shaped result
/// sets from one execution, which EF's FromSql cannot express. Reading them
/// with a DbDataReader keeps the single round trip that makes the procedure
/// worth having.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly AgriErpDbContext _context;

    public DashboardService(AgriErpDbContext context) => _context = context;

    public async Task<DashboardDto> GetAsync(
        DateTime? asOnDate, int topCount, int graphMonths, CancellationToken ct = default)
    {
        var dashboard = new DashboardDto();

        var connection = _context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;

        if (wasClosed)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "usp_DashboardSummary";
            command.CommandType = CommandType.StoredProcedure;

            // Enlists in the ambient EF transaction when one is open, so a
            // dashboard read inside a larger unit of work does not deadlock
            // against it.
            if (_context.Database.CurrentTransaction is { } transaction)
                command.Transaction = transaction.GetDbTransaction();

            command.Parameters.Add(new SqlParameter("@AsOnDate", SqlDbType.Date)
            {
                Value = (object?)asOnDate?.Date ?? DBNull.Value
            });
            command.Parameters.Add(new SqlParameter("@TopCount", SqlDbType.Int) { Value = topCount });
            command.Parameters.Add(new SqlParameter("@GraphMonths", SqlDbType.Int) { Value = graphMonths });

            await using var reader = await command.ExecuteReaderAsync(ct);

            // 1. headline figures
            if (await reader.ReadAsync(ct))
            {
                dashboard.Headline = new DashboardHeadlineDto
                {
                    AsOnDate          = reader.GetDateTime(reader.GetOrdinal("AsOnDate")),
                    TodaySales        = GetDecimal(reader, "TodaySales"),
                    TodayInvoiceCount = GetInt64(reader, "TodayInvoiceCount"),
                    TodayProfit       = GetDecimal(reader, "TodayProfit"),
                    MonthSales        = GetDecimal(reader, "MonthSales"),
                    MonthProfit       = GetDecimal(reader, "MonthProfit"),
                    TodayPurchase     = GetDecimal(reader, "TodayPurchase"),
                    MonthPurchase     = GetDecimal(reader, "MonthPurchase"),
                    StockValueAtCost  = GetDecimal(reader, "StockValueAtCost"),
                    StockValueAtMrp   = GetDecimal(reader, "StockValueAtMrp"),
                    CustomerDue       = GetDecimal(reader, "CustomerDue"),
                    SupplierDue       = GetDecimal(reader, "SupplierDue"),
                    MonthExpenses     = GetDecimal(reader, "MonthExpenses")
                };
            }

            // 2. stock alerts
            if (await reader.NextResultAsync(ct) && await reader.ReadAsync(ct))
            {
                dashboard.Alerts = new DashboardAlertsDto
                {
                    LowStockCount        = GetInt64(reader, "LowStockCount"),
                    OutOfStockCount      = GetInt64(reader, "OutOfStockCount"),
                    ExpiredBatchCount    = GetInt64(reader, "ExpiredBatchCount"),
                    NearExpiryBatchCount = GetInt64(reader, "NearExpiryBatchCount"),
                    ExpiredStockValue    = GetDecimal(reader, "ExpiredStockValue"),
                    ActiveItemCount   = GetInt64(reader, "ActiveItemCount")
                };
            }

            // 3. recent bills
            var recentBills = new List<DashboardRecentBillDto>();
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    recentBills.Add(new DashboardRecentBillDto
                    {
                        SaleId         = GetInt64(reader, "SaleId"),
                        InvoiceNumber  = GetString(reader, "InvoiceNumber"),
                        InvoiceDate    = reader.GetDateTime(reader.GetOrdinal("InvoiceDate")),
                        CustomerName   = GetString(reader, "CustomerName"),
                        Village        = GetString(reader, "Village"),
                        SaleType       = GetString(reader, "SaleType"),
                        PaymentType    = GetString(reader, "PaymentType"),
                        GrandTotal     = GetDecimal(reader, "GrandTotal"),
                        ReceivedAmount = GetDecimal(reader, "ReceivedAmount"),
                        BalanceAmount  = GetDecimal(reader, "BalanceAmount"),
                        PaymentStatus  = GetString(reader, "PaymentStatus")
                    });
                }
            }
            dashboard.RecentBills = recentBills;

            // 4. top selling items
            var topItems = new List<DashboardTopItemDto>();
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    topItems.Add(new DashboardTopItemDto
                    {
                        ItemId    = GetInt32(reader, "ItemId"),
                        ItemCode  = GetString(reader, "ItemCode"),
                        ItemName  = GetString(reader, "ItemName"),
                        ItemSubGroupName = GetString(reader, "ItemSubGroupName"),
                        CompanyName  = GetString(reader, "CompanyName"),
                        UnitCode     = GetString(reader, "UnitCode"),
                        QuantitySold = GetDecimal(reader, "QuantitySold"),
                        SalesValue   = GetDecimal(reader, "SalesValue"),
                        Profit       = GetDecimal(reader, "Profit")
                    });
                }
            }
            dashboard.TopItems = topItems;

            // 5. monthly trend
            var monthlyTrend = new List<DashboardMonthPointDto>();
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    monthlyTrend.Add(new DashboardMonthPointDto
                    {
                        MonthStart     = reader.GetDateTime(reader.GetOrdinal("MonthStart")),
                        MonthLabel     = GetString(reader, "MonthLabel"),
                        SalesAmount    = GetDecimal(reader, "SalesAmount"),
                        ProfitAmount   = GetDecimal(reader, "ProfitAmount"),
                        PurchaseAmount = GetDecimal(reader, "PurchaseAmount"),
                        ExpenseAmount  = GetDecimal(reader, "ExpenseAmount")
                    });
                }
            }
            dashboard.MonthlyTrend = monthlyTrend;

            // 6. itemSubGroup-wise stock
            var itemSubGroupStock = new List<DashboardItemSubGroupStockDto>();
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    itemSubGroupStock.Add(new DashboardItemSubGroupStockDto
                    {
                        ItemSubGroupId       = GetInt32(reader, "ItemSubGroupId"),
                        ItemSubGroupName     = GetString(reader, "ItemSubGroupName"),
                        ItemCount     = GetInt64(reader, "ItemCount"),
                        InStockCount     = GetInt32(reader, "InStockCount"),
                        OutOfStockCount  = GetInt32(reader, "OutOfStockCount"),
                        LowStockCount    = GetInt32(reader, "LowStockCount"),
                        TotalQuantity    = GetDecimal(reader, "TotalQuantity"),
                        StockValueAtCost = GetDecimal(reader, "StockValueAtCost"),
                        StockValueAtMrp  = GetDecimal(reader, "StockValueAtMrp")
                    });
                }
            }
            dashboard.ItemSubGroupStock = itemSubGroupStock;
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }

        return dashboard;
    }

    // SUM() over no rows returns NULL, and several dashboard tiles legitimately
    // have no rows on a quiet day. These readers turn that into 0 rather than
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
