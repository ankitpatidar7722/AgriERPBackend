using AgriERP.API.Authorization;
using AgriERP.Application.Features.Items;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// Item groups and the form definition each one drives.
///
/// Gated on the item VIEW permission rather than a maintenance one: this is
/// what the item screen reads before it can draw anything, so a storekeeper
/// who may list items must be able to fetch it.
/// </summary>
// Explicit, because [controller] would give "/api/itemgroups". The rest of the
// API hyphenates multi-word paths - /api/lookups/gst-slabs, /api/payment-modes
// - and one endpoint that does not is the one people get wrong.
[Route("api/item-groups")]
public class ItemGroupsController : BaseApiController
{
    private readonly IItemGroupService _groups;

    public ItemGroupsController(IItemGroupService groups) => _groups = groups;

    /// <summary>Product Master, Fertilizers Master, Seed Master, Other Master.</summary>
    [HttpGet]
    [HasPermission(Permissions.Item.View)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Success(await _groups.GetGroupsAsync(ct));

    /// <summary>
    /// The fields this group shows, in draw order, with their labels, types,
    /// validation bounds and lookup names. The client renders the item form
    /// from this rather than from a hardcoded layout, which is the whole point
    /// of the group: a seed and a pesticide ask different questions.
    /// </summary>
    [HttpGet("{id:int}/form")]
    [HasPermission(Permissions.Item.View)]
    public async Task<IActionResult> GetForm(int id, CancellationToken ct)
    {
        var form = await _groups.GetFormAsync(id, ct);
        return form is null ? NotFound($"Item group {id} was not found.") : Success(form);
    }
}
