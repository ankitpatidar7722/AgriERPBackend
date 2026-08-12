using AgriERP.API.Authorization;
using AgriERP.Application.Features.Dashboard;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

public class DashboardController : BaseApiController
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    /// <summary>
    /// The whole dashboard in one call: headline figures, stock alerts, recent
    /// bills, top sellers, the monthly trend series and itemSubGroup-wise stock.
    /// </summary>
    [HasPermission(Permissions.Dashboard.View)]
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateTime? asOnDate,
        [FromQuery] int topCount = 10,
        [FromQuery] int graphMonths = 12,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken ct = default)
    {
        // Clamped so a hand-edited query string cannot ask the procedure for a
        // 500-month recursive series.
        topCount = Math.Clamp(topCount, 1, 50);
        graphMonths = Math.Clamp(graphMonths, 1, 36);

        // fromDate/toDate scope the period figures + top items + trend to an
        // arbitrary window; when omitted the procedure falls back to the
        // calendar month of asOnDate, so this stays backward compatible.
        var dashboard = await _dashboard.GetAsync(asOnDate, topCount, graphMonths, fromDate, toDate, ct);

        // Profit is stripped for anyone without Dashboard.ViewProfit, so a
        // salesman sees turnover and stock alerts but not the shop's margin.
        if (!User.HasClaim(AgriClaimTypes.Permission, Permissions.Dashboard.ViewProfit))
        {
            dashboard.Headline.TodayProfit = 0m;
            dashboard.Headline.MonthProfit = 0m;

            foreach (var point in dashboard.MonthlyTrend)
                point.ProfitAmount = 0m;

            foreach (var item in dashboard.TopItems)
                item.Profit = 0m;
        }

        return Success(dashboard);
    }
}
