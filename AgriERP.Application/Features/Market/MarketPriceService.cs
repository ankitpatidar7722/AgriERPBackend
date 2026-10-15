using System.Globalization;
using System.Text;
using System.Text.Json;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Market.Dtos;
using AgriERP.Domain.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AgriERP.Application.Features.Market;

public interface IMarketPriceService
{
    Task<MarketPriceResultDto> GetAsync(
        string? state, string? district, string? commodity, int limit, CancellationToken ct = default);
}

/// <summary>
/// Proxies the Agmarknet daily mandi-price dataset on data.gov.in. The key is
/// read from config (DataGovIn:ApiKey) and never leaves the server. Returns
/// Configured=false when the key is unset so the widget can prompt for setup.
/// </summary>
public class MarketPriceService : IMarketPriceService
{
    // Agmarknet "Current Daily Price of Various Commodities from Various Markets".
    private const string ResourceId = "9ef84268-d588-465a-a308-a864a43d0070";

    // One long-lived client for a single external host - the documented
    // alternative to IHttpClientFactory, and enough for a few calls a day.
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private readonly IConfiguration _config;
    private readonly IUnitOfWork _uow;

    public MarketPriceService(IConfiguration config, IUnitOfWork uow)
    {
        _config = config;
        _uow = uow;
    }

    public async Task<MarketPriceResultDto> GetAsync(
        string? state, string? district, string? commodity, int limit, CancellationToken ct = default)
    {
        // Prefer the key set from the Invoice & Licences screen; fall back to config.
        var key = await _uow.Repository<CompanyProfile>().Query()
            .Select(c => c.DataGovInApiKey)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(key))
            key = _config["DataGovIn:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return new MarketPriceResultDto { Configured = false };

        limit = limit is < 1 or > 200 ? 50 : limit;

        var url = new StringBuilder(
            $"https://api.data.gov.in/resource/{ResourceId}?api-key={Uri.EscapeDataString(key)}&format=json&limit={limit}");
        void Filter(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                url.Append($"&filters[{field}]={Uri.EscapeDataString(value.Trim())}");
        }
        Filter("state", state);
        Filter("district", district);
        Filter("commodity", commodity);

        try
        {
            using var res = await _http.GetAsync(url.ToString(), ct);
            if (!res.IsSuccessStatusCode)
                return new MarketPriceResultDto { Configured = true };

            var json = await res.Content.ReadAsStringAsync(ct);
            return new MarketPriceResultDto { Configured = true, Rows = Parse(json) };
        }
        catch
        {
            // Network/parse failure - treat as "configured but nothing right now".
            return new MarketPriceResultDto { Configured = true };
        }
    }

    private static List<MarketPriceRowDto> Parse(string json)
    {
        var rows = new List<MarketPriceRowDto>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("records", out var recs) || recs.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var r in recs.EnumerateArray())
        {
            // Field names vary in case across dataset versions - index them
            // case-insensitively so the mapping is stable either way.
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in r.EnumerateObject())
                map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : p.Value.ToString();

            string Get(string k) => map.TryGetValue(k, out var v) ? v.Trim() : "";
            decimal Dec(string s) =>
                decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

            rows.Add(new MarketPriceRowDto
            {
                State = Get("state"),
                District = Get("district"),
                Market = Get("market"),
                Commodity = Get("commodity"),
                Variety = Get("variety"),
                Grade = Get("grade"),
                ArrivalDate = Get("arrival_date"),
                MinPrice = Dec(Get("min_price")),
                MaxPrice = Dec(Get("max_price")),
                ModalPrice = Dec(Get("modal_price")),
            });
        }
        return rows;
    }
}
