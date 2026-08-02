using AgriERP.API.Authorization;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

public class SuppliersController : BaseApiController
{
    private readonly ISupplierService _suppliers;

    public SuppliersController(ISupplierService suppliers) => _suppliers = suppliers;

    [HasPermission(Permissions.Supplier.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] SupplierQueryParameters parameters, CancellationToken ct)
        => Success(await _suppliers.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Supplier.View)]
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup(CancellationToken ct)
        => Success(await _suppliers.GetLookupAsync(ct));

    /// <summary>Payables ageing feed, derived from vw_SupplierOutstanding.</summary>
    [HasPermission(Permissions.Report.Party)]
    [HttpGet("outstanding")]
    public async Task<IActionResult> GetOutstanding(CancellationToken ct)
        => Success(await _suppliers.GetOutstandingAsync(ct));

    [HasPermission(Permissions.Supplier.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _suppliers.GetByIdAsync(id, ct));

    [HasPermission(Permissions.Supplier.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveSupplierRequest request, CancellationToken ct)
        => SuccessCreated(await _suppliers.CreateAsync(request, ct), "Supplier created.");

    [HasPermission(Permissions.Supplier.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveSupplierRequest request, CancellationToken ct)
        => Success(await _suppliers.UpdateAsync(id, request, ct), "Supplier updated.");

    [HasPermission(Permissions.Supplier.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _suppliers.DeleteAsync(id, ct);
        return Success("Supplier deleted.");
    }
}
