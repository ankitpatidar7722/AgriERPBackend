using AgriERP.API.Authorization;
using AgriERP.Application.Features.Stock;
using AgriERP.Application.Features.Stock.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

[Route("api/stock")]
public class StockController : BaseApiController
{
    private readonly IStockService _stock;

    public StockController(IStockService stock) => _stock = stock;

    /// <summary>Movement journal with running balance. The audit trail for any stock dispute.</summary>
    [HasPermission(Permissions.Stock.View)]
    [HttpGet("ledger")]
    public async Task<IActionResult> GetLedger([FromQuery] StockLedgerQueryParameters parameters, CancellationToken ct)
        => Success(await _stock.GetLedgerAsync(parameters, ct));

    [HasPermission(Permissions.Stock.View)]
    [HttpGet("batches")]
    public async Task<IActionResult> GetBatchStock(
        [FromQuery] int? itemId, [FromQuery] int? locationId, CancellationToken ct)
        => Success(await _stock.GetBatchStockAsync(itemId, locationId, ct));

    /// <summary>
    /// Loads starting stock as a posted adjustment. Posts immediately - opening
    /// stock has nothing to review against.
    /// </summary>
    [HasPermission(Permissions.Stock.Opening)]
    [HttpPost("opening")]
    public async Task<IActionResult> CreateOpeningStock(OpeningStockRequest request, CancellationToken ct)
        => SuccessCreated(await _stock.CreateOpeningStockAsync(request, ct), "Opening stock loaded.");

    /* ---------------- adjustments ---------------- */

    [HasPermission(Permissions.Stock.View)]
    [HttpGet("adjustments")]
    public async Task<IActionResult> GetAdjustments(
        [FromQuery] StockAdjustmentQueryParameters parameters, CancellationToken ct)
        => Success(await _stock.GetAdjustmentsAsync(parameters, ct));

    [HasPermission(Permissions.Stock.View)]
    [HttpGet("adjustments/{id:long}")]
    public async Task<IActionResult> GetAdjustment(long id, CancellationToken ct)
        => Success(await _stock.GetAdjustmentAsync(id, ct));

    /// <summary>Creates a DRAFT. A physical count moves no stock until it is posted.</summary>
    [HasPermission(Permissions.Stock.Adjust)]
    [HttpPost("adjustments")]
    public async Task<IActionResult> CreateAdjustment(SaveStockAdjustmentRequest request, CancellationToken ct)
        => SuccessCreated(await _stock.CreateAdjustmentAsync(request, ct), "Adjustment saved as draft.");

    [HasPermission(Permissions.Stock.Post)]
    [HttpPost("adjustments/{id:long}/post")]
    public async Task<IActionResult> PostAdjustment(long id, CancellationToken ct)
        => Success(await _stock.PostAdjustmentAsync(id, ct), "Adjustment posted.");

    /* ---------------- transfers ---------------- */

    [HasPermission(Permissions.Stock.View)]
    [HttpGet("transfers")]
    public async Task<IActionResult> GetTransfers(
        [FromQuery] StockTransferQueryParameters parameters, CancellationToken ct)
        => Success(await _stock.GetTransfersAsync(parameters, ct));

    [HasPermission(Permissions.Stock.View)]
    [HttpGet("transfers/{id:long}")]
    public async Task<IActionResult> GetTransfer(long id, CancellationToken ct)
        => Success(await _stock.GetTransferAsync(id, ct));

    [HasPermission(Permissions.Stock.Transfer)]
    [HttpPost("transfers")]
    public async Task<IActionResult> CreateTransfer(SaveStockTransferRequest request, CancellationToken ct)
        => SuccessCreated(await _stock.CreateTransferAsync(request, ct), "Transfer saved as draft.");

    [HasPermission(Permissions.Stock.Post)]
    [HttpPost("transfers/{id:long}/post")]
    public async Task<IActionResult> PostTransfer(long id, CancellationToken ct)
        => Success(await _stock.PostTransferAsync(id, ct), "Transfer posted.");
}
