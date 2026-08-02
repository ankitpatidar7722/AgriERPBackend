using AgriERP.API.Authorization;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

// Explicit, because [controller] would give "/api/itemsubgroups". Multi-word
// paths are hyphenated everywhere else in this API.
[Route("api/item-subgroups")]
public class ItemSubGroupsController : BaseApiController
{
    private readonly IItemSubGroupService _itemSubGroups;

    public ItemSubGroupsController(IItemSubGroupService itemSubGroups) => _itemSubGroups = itemSubGroups;

    [HasPermission(Permissions.ItemSubGroup.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] ItemSubGroupQueryParameters parameters, CancellationToken ct)
        => Success(await _itemSubGroups.GetPagedAsync(parameters, ct));

    /// <summary>
    /// Dropdown feed. Guarded by Item.View as well as ItemSubGroup.View - a
    /// salesman needs the itemSubGroup list to filter the billing screen but has
    /// no business on the itemSubGroup maintenance page.
    /// </summary>
    [HasPermission(Permissions.Item.View)]
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup(CancellationToken ct)
        => Success(await _itemSubGroups.GetLookupAsync(ct));

    [HasPermission(Permissions.ItemSubGroup.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _itemSubGroups.GetByIdAsync(id, ct));

    [HasPermission(Permissions.ItemSubGroup.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveItemSubGroupRequest request, CancellationToken ct)
        => SuccessCreated(await _itemSubGroups.CreateAsync(request, ct), "ItemSubGroup created.");

    [HasPermission(Permissions.ItemSubGroup.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveItemSubGroupRequest request, CancellationToken ct)
        => Success(await _itemSubGroups.UpdateAsync(id, request, ct), "ItemSubGroup updated.");

    [HasPermission(Permissions.ItemSubGroup.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _itemSubGroups.DeleteAsync(id, ct);
        return Success("ItemSubGroup deleted.");
    }
}
