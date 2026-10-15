namespace AgriERP.Application.Features.Market.Dtos;

/// <summary>One mandi price row from Agmarknet (data.gov.in). Prices are ₹/quintal.</summary>
public class MarketPriceRowDto
{
    public string State { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string Commodity { get; set; } = string.Empty;
    public string Variety { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string ArrivalDate { get; set; } = string.Empty;
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public decimal ModalPrice { get; set; }
}

/// <summary>
/// Mandi price result. <see cref="Configured"/> is false when no data.gov.in API
/// key is set, so the UI can prompt the shopkeeper instead of showing an error.
/// </summary>
public class MarketPriceResultDto
{
    public bool Configured { get; set; }
    public IReadOnlyList<MarketPriceRowDto> Rows { get; set; } = Array.Empty<MarketPriceRowDto>();
}
