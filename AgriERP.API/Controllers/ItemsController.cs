using AgriERP.API.Authorization;
using AgriERP.Application.Features.Items;
using AgriERP.Application.Features.Items.Dtos;
using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

public class ItemsController : BaseApiController
{
    private readonly IItemService _items;

    public ItemsController(IItemService items) => _items = items;

    /// <summary>
    /// Paged item list with search, itemSubGroup/company/GST filters, stock
    /// status and near-expiry filters, and sorting.
    /// </summary>
    [HasPermission(Permissions.Item.View)]
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] ItemQueryParameters parameters, CancellationToken ct)
        => Success(await _items.GetPagedAsync(parameters, ct));

    /// <summary>Type-ahead for billing: rates, GST, HSN and live stock in one payload.</summary>
    [HasPermission(Permissions.Item.View)]
    [HttpGet("search")]
    public async Task<IActionResult> SearchForBilling([FromQuery] string? search, CancellationToken ct)
        => Success(await _items.SearchForBillingAsync(search, ct));

    /// <summary>Barcode scan. Returns 404 so the scanner UI can beep on an unknown code.</summary>
    [HasPermission(Permissions.Item.View)]
    [HttpGet("by-barcode/{barcode}")]
    public async Task<IActionResult> FindByBarcode(string barcode, CancellationToken ct)
    {
        var item = await _items.FindByBarcodeAsync(barcode, ct);
        return item is null
            ? NotFound(Shared.Models.ApiResponse.Fail($"No item found for barcode {barcode}."))
            : Success(item);
    }

    /// <summary>Full item with its batches, in FEFO order.</summary>
    [HasPermission(Permissions.Item.View)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Success(await _items.GetByIdAsync(id, ct));

    [HasPermission(Permissions.Item.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(SaveItemRequest request, CancellationToken ct)
        => SuccessCreated(await _items.CreateAsync(request, ct), "Item created.");

    /// <summary>Bulk-imports items from an Excel upload (parsed client-side into full item rows).</summary>
    [HasPermission(Permissions.Item.Create)]
    [HttpPost("bulk-import")]
    public async Task<IActionResult> BulkImport(List<SaveItemRequest> rows, CancellationToken ct)
        => Success(await _items.BulkImportAsync(rows, ct), "Bulk import finished.");

    [HasPermission(Permissions.Item.Edit)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveItemRequest request, CancellationToken ct)
        => Success(await _items.UpdateAsync(id, request, ct), "Item updated.");

    [HasPermission(Permissions.Item.Delete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _items.DeleteAsync(id, ct);
        return Success("Item deleted.");
    }
}
