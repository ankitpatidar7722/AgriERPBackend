using AgriERP.API.Authorization;
using AgriERP.Application.Features.Reports;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

public class ReportsController : BaseApiController
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    /* ---------------- inventory ---------------- */

    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/current")]
    public async Task<IActionResult> CurrentStock(
        [FromQuery] int? itemSubGroupId, [FromQuery] int? companyId, CancellationToken ct)
        => Success(await _reports.GetCurrentStockAsync(itemSubGroupId, companyId, ct));

    /// <summary>Reorder list, deepest shortfall first.</summary>
    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/low")]
    public async Task<IActionResult> LowStock(CancellationToken ct)
        => Success(await _reports.GetLowStockAsync(ct));

    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/out-of-stock")]
    public async Task<IActionResult> OutOfStock(CancellationToken ct)
        => Success(await _reports.GetOutOfStockAsync(ct));

    /// <summary>Batches expiring inside the window - stock to push before it is a write-off.</summary>
    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/near-expiry")]
    public async Task<IActionResult> NearExpiry([FromQuery] int withinDays = 90, CancellationToken ct = default)
        => Success(await _reports.GetNearExpiryAsync(withinDays, ct));

    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/expired")]
    public async Task<IActionResult> Expired(CancellationToken ct)
        => Success(await _reports.GetExpiredAsync(ct));

    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/by-itemSubGroup")]
    public async Task<IActionResult> ItemSubGroupWiseStock(CancellationToken ct)
        => Success(await _reports.GetItemSubGroupWiseStockAsync(ct));

    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/by-company")]
    public async Task<IActionResult> CompanyWiseStock(CancellationToken ct)
        => Success(await _reports.GetCompanyWiseStockAsync(ct));

    [HasPermission(Permissions.Report.Stock)]
    [HttpGet("stock/valuation")]
    public async Task<IActionResult> StockValuation(CancellationToken ct)
        => Success(await _reports.GetStockValuationAsync(ct));

    /* ---------------- financial ---------------- */

    [HasPermission(Permissions.Report.Sales)]
    [HttpGet("sales")]
    public async Task<IActionResult> SalesReport([FromQuery] DateRangeRequest range, CancellationToken ct)
        => Success(await _reports.GetSalesReportAsync(range, ct));

    [HasPermission(Permissions.Report.Purchase)]
    [HttpGet("purchase")]
    public async Task<IActionResult> PurchaseReport([FromQuery] DateRangeRequest range, CancellationToken ct)
        => Success(await _reports.GetPurchaseReportAsync(range, ct));

    [HasPermission(Permissions.Report.Profit)]
    [HttpGet("profit")]
    public async Task<IActionResult> ProfitReport([FromQuery] DateRangeRequest range, CancellationToken ct)
        => Success(await _reports.GetProfitReportAsync(range, ct));

    [HasPermission(Permissions.Report.Profit)]
    [HttpGet("profit/by-item")]
    public async Task<IActionResult> ItemProfit(
        [FromQuery] DateRangeRequest range, [FromQuery] int topCount = 25, CancellationToken ct = default)
        => Success(await _reports.GetItemProfitAsync(range, topCount, ct));

    /// <summary>
    /// HSN-wise output and input tax for the period. An indicative working
    /// figure for the shop, not a filed return - reverse charge and ineligible
    /// credit are the accountant's call.
    /// </summary>
    [HasPermission(Permissions.Report.Gst)]
    [HttpGet("gst")]
    public async Task<IActionResult> GstReturn([FromQuery] DateRangeRequest range, CancellationToken ct)
        => Success(await _reports.GetGstReturnAsync(range, ct));
}
