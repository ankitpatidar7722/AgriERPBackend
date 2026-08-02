using AgriERP.Application.Features.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// The sidebar, served from ModuleMaster.
///
/// Only [Authorize], not permission-gated: this endpoint IS the permission
/// filter. Gating it behind a maintenance permission would leave every ordinary
/// user staring at an empty shell, and the answer is already scoped to the
/// caller - the service returns only what that user's permissions allow.
/// </summary>
[Authorize]
public class ModulesController : BaseApiController
{
    private readonly IModuleService _modules;

    public ModulesController(IModuleService modules) => _modules = modules;

    /// <summary>
    /// Menu groups in ModuleHeadDisplayOrder, entries within each in
    /// ModuleDisplayOrder. Retired rows (IsDeletedTransaction = 1) are excluded.
    /// </summary>
    [HttpGet("sidebar")]
    public async Task<IActionResult> GetSidebar(CancellationToken ct)
        => Success(await _modules.GetSidebarAsync(ct));
}
