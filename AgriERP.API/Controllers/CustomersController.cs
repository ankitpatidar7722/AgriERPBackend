using AgriERP.API.Authorization;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

public class CustomersController : BaseApiController
{
    private readonly ICustomerService _customers;

    public CustomersController(ICustomerService customers) => _customers = customers;

    [HasPermission(Permissions.Customer.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] CustomerQueryParameters parameters, CancellationToken ct)
        => Success(await _customers.GetPagedAsync(parameters, ct));

    /// <summary>Type-ahead for the billing screen. Capped at 50 rows server-side.</summary>
    [HasPermission(Permissions.Customer.View)]
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup([FromQuery] string? search, CancellationToken ct)
        => Success(await _customers.GetLookupAsync(search, ct));

    /// <summary>
    /// The counter's fastest path: a farmer gives a mobile number and the
    /// whole record - including dues - comes back in one call.
    /// </summary>
    [HasPermission(Permissions.Customer.View)]
    [HttpGet("by-mobile/{mobile}")]
    public async Task<IActionResult> FindByMobile(string mobile, CancellationToken ct)
    {
        var customer = await _customers.FindByMobileAsync(mobile, ct);
        return customer is null
            ? NotFound(Shared.Models.ApiResponse.Fail($"No customer found with mobile {mobile}."))
            : Success(customer);
    }

    /// <summary>Distinct villages on file, for the village filter dropdown.</summary>
    [HasPermission(Permissions.Customer.View)]
    [HttpGet("villages")]
    public async Task<IActionResult> GetVillages(CancellationToken ct)
        => Success(await _customers.GetVillagesAsync(ct));

    /// <summary>Receivables ageing feed, derived from vw_CustomerOutstanding.</summary>
    [HasPermission(Permissions.Report.Party)]
    [HttpGet("outstanding")]
    public async Task<IActionResult> GetOutstanding(CancellationToken ct)
        => Success(await _customers.GetOutstandingAsync(ct));

    /// <summary>Receivables headline (total dues, today's collection, overdue, near-due) for the dashboard.</summary>
    [HasPermission(Permissions.Customer.View)]
    [HttpGet("receivables-summary")]
    public async Task<IActionResult> GetReceivablesSummary(CancellationToken ct)
        => Success(await _customers.GetReceivablesSummaryAsync(ct));

    [HasPermission(Permissions.Customer.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _customers.GetByIdAsync(id, ct));

    /// <summary>Customer profile header: identity + money summary, all derived from the ledger.</summary>
    [HasPermission(Permissions.Customer.View)]
    [HttpGet("{id:int}/profile")]
    public async Task<IActionResult> GetProfile(int id, CancellationToken ct)
        => Success(await _customers.GetProfileAsync(id, ct));

    /// <summary>The Tally-style customer money ledger (opening, vouchers, running balance, closing).</summary>
    [HasPermission(Permissions.Customer.View)]
    [HttpGet("{id:int}/ledger")]
    public async Task<IActionResult> GetLedger(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _customers.GetLedgerAsync(id, from, to, ct));

    [HasPermission(Permissions.Customer.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveCustomerRequest request, CancellationToken ct)
        => SuccessCreated(await _customers.CreateAsync(request, ct), "Customer created.");

    /// <summary>Bulk-imports customers from an Excel upload (parsed client-side into rows).</summary>
    [HasPermission(Permissions.Customer.Create)]
    [HttpPost("bulk-import")]
    public async Task<IActionResult> BulkImport(List<SaveCustomerRequest> rows, CancellationToken ct)
        => Success(await _customers.BulkImportAsync(rows, ct), "Bulk import finished.");

    [HasPermission(Permissions.Customer.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveCustomerRequest request, CancellationToken ct)
        => Success(await _customers.UpdateAsync(id, request, ct), "Customer updated.");

    [HasPermission(Permissions.Customer.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _customers.DeleteAsync(id, ct);
        return Success("Customer deleted.");
    }
}
