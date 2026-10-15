using AgriERP.API.Authorization;
using AgriERP.Application.Features.Settings;
using AgriERP.Application.Features.Settings.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// The shop's own invoice-header identity - UPI id, dealer licences, bank
/// details, invoice terms/footer. A single row (CompanyProfile singleton),
/// distinct from the editable ShopMaster list.
/// </summary>
[Route("api/company-profile")]
public class CompanyProfileController : BaseApiController
{
    private readonly ICompanyProfileService _profiles;

    public CompanyProfileController(ICompanyProfileService profiles) => _profiles = profiles;

    [HasPermission(Permissions.Settings.View)]
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Success(await _profiles.GetAsync(ct));

    [HasPermission(Permissions.Settings.Edit)]
    [HttpPut]
    public async Task<IActionResult> Update(SaveCompanyProfileRequest request, CancellationToken ct)
        => Success(await _profiles.UpdateAsync(request, ct), "Invoice settings saved.");
}
