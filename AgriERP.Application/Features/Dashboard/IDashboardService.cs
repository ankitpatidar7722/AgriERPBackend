namespace AgriERP.Application.Features.Dashboard;

public class DashboardHeadlineDto
{
    public DateTime AsOnDate { get; set; }
    public decimal TodaySales { get; set; }
    public long TodayInvoiceCount { get; set; }
    public decimal TodayProfit { get; set; }
    public decimal MonthSales { get; set; }
    public decimal MonthProfit { get; set; }
    public decimal TodayPurchase { get; set; }
    public decimal MonthPurchase { get; set; }
    public decimal StockValueAtCost { get; set; }
    public decimal StockValueAtMrp { get; set; }
    public decimal CustomerDue { get; set; }
    public decimal SupplierDue { get; set; }
    public decimal MonthExpenses { get; set; }
}

public class DashboardAlertsDto
{
    public long LowStockCount { get; set; }
    public long OutOfStockCount { get; set; }
    public long ExpiredBatchCount { get; set; }
    public long NearExpiryBatchCount { get; set; }
    public decimal ExpiredStockValue { get; set; }
    public long ActiveItemCount { get; set; }
}

public class DashboardRecentBillDto
{
    public long SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public string SaleType { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class DashboardTopItemDto
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public decimal QuantitySold { get; set; }
    public decimal SalesValue { get; set; }
    public decimal Profit { get; set; }
}

public class DashboardMonthPointDto
{
    public DateTime MonthStart { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
    public decimal SalesAmount { get; set; }
    public decimal ProfitAmount { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal ExpenseAmount { get; set; }
}

public class DashboardItemSubGroupStockDto
{
    public int ItemSubGroupId { get; set; }
    public string ItemSubGroupName { get; set; } = string.Empty;
    public long ItemCount { get; set; }
    public int InStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int LowStockCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal StockValueAtCost { get; set; }
    public decimal StockValueAtMrp { get; set; }
}

public class DashboardDto
{
    public DashboardHeadlineDto Headline { get; set; } = new();
    public DashboardAlertsDto Alerts { get; set; } = new();
    public IReadOnlyList<DashboardRecentBillDto> RecentBills { get; set; } = Array.Empty<DashboardRecentBillDto>();
    public IReadOnlyList<DashboardTopItemDto> TopItems { get; set; } = Array.Empty<DashboardTopItemDto>();
    public IReadOnlyList<DashboardMonthPointDto> MonthlyTrend { get; set; } = Array.Empty<DashboardMonthPointDto>();
    public IReadOnlyList<DashboardItemSubGroupStockDto> ItemSubGroupStock { get; set; } = Array.Empty<DashboardItemSubGroupStockDto>();
}

/// <summary>
/// Backed by usp_DashboardSummary, which returns all six blocks in one
/// round trip. Thirteen separate queries from the API would each pay their own
/// latency and connection cost to render one screen.
/// </summary>
public interface IDashboardService
{
    Task<DashboardDto> GetAsync(DateTime? asOnDate, int topCount, int graphMonths, CancellationToken ct = default);
}
