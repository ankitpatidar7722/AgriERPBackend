using AgriERP.API.Authorization;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>Warehouse master - auto-coded (WH00001), with a list of bins. Standalone (separate from StorageLocations).</summary>
public class WarehousesController : BaseApiController
{
    private readonly IWarehouseService _warehouses;

    public WarehousesController(IWarehouseService warehouses) => _warehouses = warehouses;

    [HasPermission(Permissions.Settings.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] WarehouseQueryParameters parameters, CancellationToken ct)
        => Success(await _warehouses.GetPagedAsync(parameters, ct));

    /// <summary>Active warehouses for the GRN dropdown. Readable by anyone who can see purchases.</summary>
    [HasPermission(Permissions.Purchase.View)]
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup(CancellationToken ct)
        => Success(await _warehouses.GetLookupAsync(ct));

    /// <summary>Indicative next warehouse code for the create form. Does not consume the series.</summary>
    [HasPermission(Permissions.Settings.View)]
    [HttpGet("next-code")]
    public async Task<IActionResult> PeekNextCode(CancellationToken ct)
        => Success(new { code = await _warehouses.PeekNextCodeAsync(ct) });

    [HasPermission(Permissions.Settings.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _warehouses.GetByIdAsync(id, ct));

    [HasPermission(Permissions.Settings.Edit)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveWarehouseRequest request, CancellationToken ct)
        => SuccessCreated(await _warehouses.CreateAsync(request, ct), "Warehouse saved.");

    [HasPermission(Permissions.Settings.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveWarehouseRequest request, CancellationToken ct)
        => Success(await _warehouses.UpdateAsync(id, request, ct), "Warehouse updated.");

    [HasPermission(Permissions.Settings.Edit)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _warehouses.DeleteAsync(id, ct);
        return Success("Warehouse deleted.");
    }
}
