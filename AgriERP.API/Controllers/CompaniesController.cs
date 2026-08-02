using AgriERP.API.Authorization;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>Manufacturers - UPL, Bayer, IFFCO. Not the shop itself.</summary>
public class CompaniesController : BaseApiController
{
    private readonly ICompanyService _companies;

    public CompaniesController(ICompanyService companies) => _companies = companies;

    [HasPermission(Permissions.Company.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] CompanyQueryParameters parameters, CancellationToken ct)
        => Success(await _companies.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Item.View)]
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup(CancellationToken ct)
        => Success(await _companies.GetLookupAsync(ct));

    [HasPermission(Permissions.Company.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _companies.GetByIdAsync(id, ct));

    [HasPermission(Permissions.Company.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveCompanyRequest request, CancellationToken ct)
        => SuccessCreated(await _companies.CreateAsync(request, ct), "Company created.");

    [HasPermission(Permissions.Company.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveCompanyRequest request, CancellationToken ct)
        => Success(await _companies.UpdateAsync(id, request, ct), "Company updated.");

    [HasPermission(Permissions.Company.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _companies.DeleteAsync(id, ct);
        return Success("Company deleted.");
    }
}
