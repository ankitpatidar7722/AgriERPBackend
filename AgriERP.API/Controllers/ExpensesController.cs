using AgriERP.API.Authorization;
using AgriERP.Application.Features.Expenses;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

/// <summary>
/// Shop running costs (rent, electricity, wages, ...) - recorded so the year's
/// gross profit can be reduced to a real net profit.
/// </summary>
public class ExpensesController : BaseApiController
{
    private readonly IExpenseService _expenses;

    public ExpensesController(IExpenseService expenses) => _expenses = expenses;

    [HasPermission(Permissions.Payment.ExpenseView)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] ExpenseQueryParameters parameters, CancellationToken ct)
        => Success(await _expenses.GetPagedAsync(parameters, ct));

    [HasPermission(Permissions.Payment.ExpenseView)]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
        => Success(await _expenses.GetByIdAsync(id, ct));

    /// <summary>Period totals (with a category breakdown) - drives net profit on
    /// the profit report. Gated with the profit permission since that is where
    /// it is shown.</summary>
    [HasPermission(Permissions.Report.Profit)]
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, CancellationToken ct)
        => Success(await _expenses.GetSummaryAsync(fromDate, toDate, ct));

    [HasPermission(Permissions.Payment.ExpenseCreate)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveExpenseRequest request, CancellationToken ct)
        => SuccessCreated(await _expenses.CreateAsync(request, ct), "Expense recorded.");

    [HasPermission(Permissions.Payment.ExpenseCreate)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveExpenseRequest request, CancellationToken ct)
        => Success(await _expenses.UpdateAsync(id, request, ct), "Expense updated.");

    [HasPermission(Permissions.Payment.ExpenseCreate)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _expenses.DeleteAsync(id, ct);
        return Success(new { deleted = true }, "Expense deleted.");
    }
}
