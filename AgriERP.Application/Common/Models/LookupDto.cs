namespace AgriERP.Application.Common.Models;

/// <summary>
/// Shape every dropdown consumes. One contract means the frontend has one
/// combo-box component rather than one per master.
/// </summary>
public class LookupDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Secondary line in the dropdown - village, pack size, GST rate.</summary>
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Lookup carrying the extra fields a billing screen needs when a item is picked.</summary>
public class ItemLookupDto : LookupDto
{
    public string? ShortName { get; set; }
    public string? Barcode { get; set; }
    /// <summary>Parent group / sub-group names, shown on the purchase-order grid.</summary>
    public string? ItemGroupName { get; set; }
    public string? ItemSubGroupName { get; set; }
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal SellingRate { get; set; }
    public decimal WholesaleRate { get; set; }
    public decimal DealerRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal MinSellingRate { get; set; }
    public decimal GstPercent { get; set; }
    public string? HsnCode { get; set; }
    public decimal CurrentStock { get; set; }
}
