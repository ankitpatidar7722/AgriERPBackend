using AgriERP.Application.Features.Market;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// Mandi (market) prices from Agmarknet via data.gov.in. Any signed-in user may
/// read them; the API key stays server-side (DataGovIn:ApiKey).
/// </summary>
[Route("api/market-prices")]
public class MarketPricesController : BaseApiController
{
    private readonly IMarketPriceService _market;

    public MarketPricesController(IMarketPriceService market) => _market = market;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? state,
        [FromQuery] string? district,
        [FromQuery] string? commodity,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Success(await _market.GetAsync(state, district, commodity, limit, ct));
}
