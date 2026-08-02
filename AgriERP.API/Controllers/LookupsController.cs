using AgriERP.Application.Features.Masters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// Reference data for dropdowns.
///
/// Only [Authorize], not permission-gated: these are lists of Indian states,
/// GST rates and HSN codes. They contain nothing about the shop, and gating
/// them behind a maintenance permission would break the item form for a
/// storekeeper who is allowed to use it.
/// </summary>
[Authorize]
public class LookupsController : BaseApiController
{
    private readonly ILookupService _lookups;

    public LookupsController(ILookupService lookups) => _lookups = lookups;

    [HttpGet("states")]
    public async Task<IActionResult> GetStates(CancellationToken ct)
        => Success(await _lookups.GetStatesAsync(ct));

    [HttpGet("gst-slabs")]
    public async Task<IActionResult> GetGstSlabs(CancellationToken ct)
        => Success(await _lookups.GetGstSlabsAsync(ct));

    [HttpGet("hsn-codes")]
    public async Task<IActionResult> GetHsnCodes(CancellationToken ct)
        => Success(await _lookups.GetHsnCodesAsync(ct));

    [HttpGet("storage-locations")]
    public async Task<IActionResult> GetStorageLocations(CancellationToken ct)
        => Success(await _lookups.GetStorageLocationsAsync(ct));

    /// <summary>Cash, UPI, card, cheque, NEFT - the tender split on a bill.</summary>
    [HttpGet("payment-modes")]
    public async Task<IActionResult> GetPaymentModes(CancellationToken ct)
        => Success(await _lookups.GetPaymentModesAsync(ct));

    /// <summary>
    /// Everything the item form needs in one round trip, instead of six
    /// parallel calls the moment the page opens.
    /// </summary>
    [HttpGet("item-form")]
    public async Task<IActionResult> GetItemFormLookups(CancellationToken ct)
        => Success(await _lookups.GetItemFormLookupsAsync(ct));
}
