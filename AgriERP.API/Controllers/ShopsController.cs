using AgriERP.API.Authorization;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>Shop/outlet master - a standalone editable list, not the invoice-header CompanyProfile.</summary>
public class ShopsController : BaseApiController
{
    private readonly IShopService _shops;

    public ShopsController(IShopService shops) => _shops = shops;

    [HasPermission(Permissions.Settings.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] ShopQueryParameters parameters, CancellationToken ct)
        => Success(await _shops.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Settings.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _shops.GetByIdAsync(id, ct));

    [HasPermission(Permissions.Settings.Edit)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveShopRequest request, CancellationToken ct)
        => SuccessCreated(await _shops.CreateAsync(request, ct), "Shop saved.");

    [HasPermission(Permissions.Settings.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveShopRequest request, CancellationToken ct)
        => Success(await _shops.UpdateAsync(id, request, ct), "Shop updated.");

    [HasPermission(Permissions.Settings.Edit)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _shops.DeleteAsync(id, ct);
        return Success("Shop deleted.");
    }
}
