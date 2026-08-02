using AgriERP.API.Authorization;
using AgriERP.Application.Features.Payments;
using AgriERP.Domain.Enums;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// Receipts from customers and payments to suppliers. One controller because
/// they are the same event with opposite signs.
/// </summary>
public class PaymentsController : BaseApiController
{
    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments) => _payments = payments;

    [HasPermission(Permissions.Payment.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] PaymentQueryParameters parameters, CancellationToken ct)
        => Success(await _payments.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Payment.View)]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
        => Success(await _payments.GetByIdAsync(id, ct));

    /// <summary>Shop header + payment + amount-in-words for a printable receipt.</summary>
    [HasPermission(Permissions.Payment.View)]
    [HttpGet("{id:long}/print")]
    public async Task<IActionResult> GetForPrint(long id, CancellationToken ct)
        => Success(await _payments.GetReceiptForPrintAsync(id, ct));

    /// <summary>
    /// Open bills for a party, oldest first - the order a collection screen
    /// should offer them, since the oldest debt is the one most at risk.
    /// </summary>
    [HasPermission(Permissions.Payment.View)]
    [HttpGet("open-bills")]
    public async Task<IActionResult> GetOpenBills(
        [FromQuery] PartyType partyType, [FromQuery] int partyId, CancellationToken ct)
        => Success(await _payments.GetOpenBillsAsync(partyType, partyId, ct));

    /// <summary>
    /// Records a receipt or payment. Anything not allocated to a bill stays as
    /// an on-account advance rather than being spread automatically.
    /// </summary>
    [HasPermission(Permissions.Payment.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SavePaymentRequest request, CancellationToken ct)
        => SuccessCreated(await _payments.CreateAsync(request, ct), "Payment recorded.");

    /// <summary>Reopens every bill this payment settled - the bounced-cheque path.</summary>
    [HasPermission(Permissions.Payment.Cancel)]
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, [FromBody] CancelDocumentRequest request, CancellationToken ct)
        => Success(await _payments.CancelAsync(id, request.Reason, ct), "Payment cancelled and bills reopened.");
}
