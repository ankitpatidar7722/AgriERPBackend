using AgriERP.API.Authorization;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// Units of measure. Maintained under the Item permissions rather than
/// getting a permission set of its own - a unit only exists to serve items,
/// and a fifth CRUD permission group for fourteen rows is noise in the role
/// screen.
/// </summary>
public class UnitsController : BaseApiController
{
    private readonly IUnitService _units;

    public UnitsController(IUnitService units) => _units = units;

    [HasPermission(Permissions.Item.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] UnitQueryParameters parameters, CancellationToken ct)
        => Success(await _units.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Item.View)]
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup(CancellationToken ct)
        => Success(await _units.GetLookupAsync(ct));

    [HasPermission(Permissions.Item.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _units.GetByIdAsync(id, ct));

    [HasPermission(Permissions.Item.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveUnitRequest request, CancellationToken ct)
        => SuccessCreated(await _units.CreateAsync(request, ct), "Unit created.");

    [HasPermission(Permissions.Item.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveUnitRequest request, CancellationToken ct)
        => Success(await _units.UpdateAsync(id, request, ct), "Unit updated.");

    [HasPermission(Permissions.Item.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _units.DeleteAsync(id, ct);
        return Success("Unit deleted.");
    }
}
