using AgriERP.API.Authorization;
using AgriERP.Application.Features.Sales;
using AgriERP.Application.Features.Sales.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

public class SalesController : BaseApiController
{
    private readonly ISalesService _sales;

    public SalesController(ISalesService sales) => _sales = sales;

    [HasPermission(Permissions.Sales.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] SaleQueryParameters parameters, CancellationToken ct)
        => Success(await _sales.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Sales.View)]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
        => Success(await _sales.GetByIdAsync(id, ct));

    /// <summary>
    /// Creates a DRAFT invoice. Batches are picked by FEFO unless the caller
    /// names one, so a single requested line may become several invoice lines
    /// when the quantity spans batches.
    /// </summary>
    [HasPermission(Permissions.Sales.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveSaleRequest request, CancellationToken ct)
        => SuccessCreated(await _sales.CreateAsync(request, ct), "Invoice saved as draft.");

    [HasPermission(Permissions.Sales.Post)]
    [HttpPost("{id:long}/post")]
    public async Task<IActionResult> Post(long id, CancellationToken ct)
        => Success(await _sales.PostAsync(id, ct), "Invoice posted and stock deducted.");

    [HasPermission(Permissions.Sales.Cancel)]
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, [FromBody] CancelDocumentRequest request, CancellationToken ct)
        => Success(await _sales.CancelAsync(id, request.Reason, ct), "Invoice cancelled and stock restored.");

    /// <summary>
    /// Everything the printed bill needs: shop header with licence numbers,
    /// lines with batch and expiry, HSN-wise tax summary, amount in words.
    /// Increments the print count so a reprint is visible.
    /// </summary>
    [HasPermission(Permissions.Sales.Print)]
    [HttpGet("{id:long}/print")]
    public async Task<IActionResult> GetForPrint(long id, CancellationToken ct)
        => Success(await _sales.GetInvoiceForPrintAsync(id, ct));

    /* ---------------- returns ---------------- */

    [HasPermission(Permissions.Sales.View)]
    [HttpGet("returns")]
    public async Task<IActionResult> GetReturns(
        [FromQuery] SalesReturnQueryParameters parameters, CancellationToken ct)
        => Success(await _sales.GetReturnsAsync(parameters, ct));

    [HasPermission(Permissions.Sales.View)]
    [HttpGet("returns/{id:long}")]
    public async Task<IActionResult> GetReturn(long id, CancellationToken ct)
        => Success(await _sales.GetReturnAsync(id, ct));

    [HasPermission(Permissions.Sales.Return)]
    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn(SaveSalesReturnRequest request, CancellationToken ct)
        => SuccessCreated(await _sales.CreateReturnAsync(request, ct), "Sales return saved as draft.");

    /// <summary>Saleable goods go back into stock; expired or damaged ones do not.</summary>
    [HasPermission(Permissions.Sales.Return)]
    [HttpPost("returns/{id:long}/post")]
    public async Task<IActionResult> PostReturn(long id, CancellationToken ct)
        => Success(await _sales.PostReturnAsync(id, ct), "Sales return posted.");
}
