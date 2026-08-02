using AgriERP.API.Authorization;
using AgriERP.Application.Features.Purchases;
using AgriERP.Application.Features.Purchases.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

public class PurchasesController : BaseApiController
{
    private readonly IPurchaseService _purchases;

    public PurchasesController(IPurchaseService purchases) => _purchases = purchases;

    [HasPermission(Permissions.Purchase.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] PurchaseQueryParameters parameters, CancellationToken ct)
        => Success(await _purchases.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Purchase.View)]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
        => Success(await _purchases.GetByIdAsync(id, ct));

    /// <summary>Shop header, bill, tax summary and amount-in-words for the printable purchase order.</summary>
    [HasPermission(Permissions.Purchase.View)]
    [HttpGet("{id:long}/print")]
    public async Task<IActionResult> GetForPrint(long id, CancellationToken ct)
        => Success(await _purchases.GetPurchaseForPrintAsync(id, ct));

    /// <summary>
    /// Creates a DRAFT bill. Nothing reaches stock until it is posted, so a
    /// half-keyed consignment can be left and finished later.
    /// </summary>
    [HasPermission(Permissions.Purchase.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SavePurchaseRequest request, CancellationToken ct)
        => SuccessCreated(await _purchases.CreateAsync(request, ct), "Purchase saved as draft.");

    /// <summary>Re-saves a DRAFT GRN. Rejected once posted - the stock has already moved.</summary>
    [HasPermission(Permissions.Purchase.Create)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, SavePurchaseRequest request, CancellationToken ct)
        => Success(await _purchases.UpdateAsync(id, request, ct), "GRN updated.");

    /// <summary>Posts to stock: creates batches, applies landed cost, updates item rates.</summary>
    [HasPermission(Permissions.Purchase.Post)]
    [HttpPost("{id:long}/post")]
    public async Task<IActionResult> Post(long id, CancellationToken ct)
        => Success(await _purchases.PostAsync(id, ct), "Purchase posted and stock updated.");

    /// <summary>Reverses the stock rather than deleting it. The journal keeps both entries.</summary>
    [HasPermission(Permissions.Purchase.Cancel)]
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, [FromBody] CancelDocumentRequest request, CancellationToken ct)
        => Success(await _purchases.CancelAsync(id, request.Reason, ct), "Purchase cancelled and stock reversed.");

    /* ---------------- returns ---------------- */

    [HasPermission(Permissions.Purchase.View)]
    [HttpGet("returns")]
    public async Task<IActionResult> GetReturns(
        [FromQuery] PurchaseReturnQueryParameters parameters, CancellationToken ct)
        => Success(await _purchases.GetReturnsAsync(parameters, ct));

    [HasPermission(Permissions.Purchase.View)]
    [HttpGet("returns/{id:long}")]
    public async Task<IActionResult> GetReturn(long id, CancellationToken ct)
        => Success(await _purchases.GetReturnAsync(id, ct));

    [HasPermission(Permissions.Purchase.Return)]
    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn(SavePurchaseReturnRequest request, CancellationToken ct)
        => SuccessCreated(await _purchases.CreateReturnAsync(request, ct), "Purchase return saved as draft.");

    [HasPermission(Permissions.Purchase.Return)]
    [HttpPost("returns/{id:long}/post")]
    public async Task<IActionResult> PostReturn(long id, CancellationToken ct)
        => Success(await _purchases.PostReturnAsync(id, ct), "Purchase return posted.");

    /* ---------------- orders ---------------- */

    [HasPermission(Permissions.Purchase.Order)]
    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] PurchaseOrderQueryParameters parameters, CancellationToken ct)
        => Success(await _purchases.GetOrdersAsync(parameters, ct));

    [HasPermission(Permissions.Purchase.Order)]
    [HttpGet("orders/{id:long}")]
    public async Task<IActionResult> GetOrder(long id, CancellationToken ct)
        => Success(await _purchases.GetOrderAsync(id, ct));

    [HasPermission(Permissions.Purchase.Order)]
    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(SavePurchaseOrderRequest request, CancellationToken ct)
        => SuccessCreated(await _purchases.CreateOrderAsync(request, ct), "Purchase order created.");

    /// <summary>Re-saves an OPEN order. Rejected once a GRN has drawn on it.</summary>
    [HasPermission(Permissions.Purchase.Order)]
    [HttpPut("orders/{id:long}")]
    public async Task<IActionResult> UpdateOrder(long id, SavePurchaseOrderRequest request, CancellationToken ct)
        => Success(await _purchases.UpdateOrderAsync(id, request, ct), "Purchase order updated.");

    /* ---------------- requisitions ---------------- */

    [HasPermission(Permissions.Purchase.Order)]
    [HttpGet("requisitions")]
    public async Task<IActionResult> GetRequisitions(
        [FromQuery] PurchaseRequisitionQueryParameters parameters, CancellationToken ct)
        => Success(await _purchases.GetRequisitionsAsync(parameters, ct));

    /// <summary>Indicative next requisition number for the create form. Does not consume the series.</summary>
    [HasPermission(Permissions.Purchase.Order)]
    [HttpGet("requisitions/next-number")]
    public async Task<IActionResult> PeekNextRequisitionNumber(CancellationToken ct)
        => Success(new { number = await _purchases.PeekNextRequisitionNumberAsync(ct) });

    [HasPermission(Permissions.Purchase.Order)]
    [HttpGet("requisitions/{id:long}")]
    public async Task<IActionResult> GetRequisition(long id, CancellationToken ct)
        => Success(await _purchases.GetRequisitionAsync(id, ct));

    [HasPermission(Permissions.Purchase.Order)]
    [HttpPost("requisitions")]
    public async Task<IActionResult> CreateRequisition(SavePurchaseRequisitionRequest request, CancellationToken ct)
        => SuccessCreated(await _purchases.CreateRequisitionAsync(request, ct), "Purchase requisition created.");

    /// <summary>Re-saves an OPEN requisition. Rejected once a PO has drawn on it.</summary>
    [HasPermission(Permissions.Purchase.Order)]
    [HttpPut("requisitions/{id:long}")]
    public async Task<IActionResult> UpdateRequisition(long id, SavePurchaseRequisitionRequest request, CancellationToken ct)
        => Success(await _purchases.UpdateRequisitionAsync(id, request, ct), "Purchase requisition updated.");

    [HasPermission(Permissions.Purchase.Order)]
    [HttpPost("requisitions/{id:long}/cancel")]
    public async Task<IActionResult> CancelRequisition(long id, CancellationToken ct)
        => Success(await _purchases.CancelRequisitionAsync(id, ct));
}

public class CancelDocumentRequest
{
    /// <summary>Required. A cancelled document must say why - it stays in the record forever.</summary>
    public string Reason { get; set; } = string.Empty;
}
