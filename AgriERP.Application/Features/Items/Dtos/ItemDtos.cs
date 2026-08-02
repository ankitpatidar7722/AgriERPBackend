using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Items.Dtos;

public class ItemListDto
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? TechnicalName { get; set; }
    public int ItemSubGroupId { get; set; }
    public string ItemSubGroupName { get; set; } = string.Empty;
    /// <summary>The parent group's name (Product/Fertilizers/Seed/Other Master) - shown in the item picker.</summary>
    public string ItemGroupName { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string? Brand { get; set; }

    // Returned as two fields rather than a pre-formatted "250 ML" string:
    // decimal.ToString() has no reliable SQL translation, and formatting is
    // the frontend's job anyway.
    public decimal? PackingSize { get; set; }
    public string? PackingUnitCode { get; set; }

    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string? HsnCode { get; set; }
    public decimal GstPercent { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SellingRate { get; set; }
    // The other price lists and the floor, carried on the list row so the sales
    // item-picker can build a bill line without a second lookup per item.
    public decimal WholesaleRate { get; set; }
    public decimal DealerRate { get; set; }
    public decimal MinSellingRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal MinStockLevel { get; set; }
    public string? RackNumber { get; set; }
    public string? Barcode { get; set; }
    public string? ImagePath { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Rolled up from batches by vw_ItemStock, not stored on the item.</summary>
    public decimal CurrentStock { get; set; }

    /// <summary>OutOfStock | LowStock | OverStock | Normal.</summary>
    public string StockStatus { get; set; } = "Normal";

    public DateTime? NearestExpiryDate { get; set; }
}

public class ItemDto : ItemListDto
{
    /// <summary>Which form this item was entered on, and must be edited on.</summary>
    public int ItemGroupId { get; set; }

    /// <summary>
    /// Group-specific values, keyed by ItemGroupFieldId - the same shape the
    /// save request takes, so the edit form round-trips without translation.
    /// </summary>
    public Dictionary<int, string?> ExtraFields { get; set; } = new();

    public int? PackingUnitId { get; set; }
    public int? PurchaseUnitId { get; set; }
    public int? StockUnitId { get; set; }
    public int? HsnId { get; set; }
    public int GstSlabId { get; set; }
    public bool IsRateInclusiveOfTax { get; set; }
    // UnitId, WholesaleRate, DealerRate and MinSellingRate now live on ItemListDto.
    public decimal MaxStockLevel { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsBatchTracked { get; set; }
    public bool IsExpiryTracked { get; set; }
    public bool AllowNegativeStock { get; set; }
    public int? DefaultLocationId { get; set; }
    public string? Description { get; set; }
    public string? LicenceNumber { get; set; }
    public decimal StockValueAtCost { get; set; }
    public int BatchCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public IReadOnlyList<ItemBatchDto> Batches { get; set; } = Array.Empty<ItemBatchDto>();
}

public class ItemBatchDto
{
    public long BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SellingRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal InwardQty { get; set; }
    public decimal OutwardQty { get; set; }
    public decimal CurrentQty { get; set; }
    public int? DaysToExpiry { get; set; }

    /// <summary>NoExpiry | Expired | Critical | Warning | Safe.</summary>
    public string ExpiryStatus { get; set; } = "NoExpiry";
}

public class SaveItemRequest
{
    /// <summary>
    /// Left blank on create; the API allocates from the group's own series, so
    /// a seed becomes S-000001 while a pesticide becomes P-000001.
    /// </summary>
    public string? ItemCode { get; set; }

    public string ItemName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? TechnicalName { get; set; }

    /// <summary>
    /// Optional. Left at 0 the group is taken from the sub-group, which is the
    /// normal path - the client picks a sub-group and the group follows.
    /// </summary>
    public int ItemGroupId { get; set; }

    /// <summary>
    /// Values for the group-specific fields that have no column of their own,
    /// keyed by ItemGroupFieldId: seed germination, fertilizer N-P-K, equipment
    /// warranty. Fields the group does not define are rejected rather than
    /// ignored, so a client sending a stale form cannot write orphaned rows.
    /// </summary>
    public Dictionary<int, string?> ExtraFields { get; set; } = new();

    public int ItemSubGroupId { get; set; }
    public int? CompanyId { get; set; }
    public string? Brand { get; set; }
    public decimal? PackingSize { get; set; }
    public int? PackingUnitId { get; set; }
    public int UnitId { get; set; }
    public int? PurchaseUnitId { get; set; }
    public int? StockUnitId { get; set; }
    public int? HsnId { get; set; }
    public int GstSlabId { get; set; }
    public bool IsRateInclusiveOfTax { get; set; }

    public decimal PurchaseRate { get; set; }
    public decimal SellingRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal WholesaleRate { get; set; }
    public decimal DealerRate { get; set; }
    public decimal MinSellingRate { get; set; }

    public decimal MinStockLevel { get; set; }
    public decimal MaxStockLevel { get; set; }
    public decimal ReorderLevel { get; set; }

    public bool IsBatchTracked { get; set; } = true;
    public bool IsExpiryTracked { get; set; } = true;
    public bool AllowNegativeStock { get; set; }

    public int? DefaultLocationId { get; set; }
    public string? ImagePath { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ItemQueryParameters : QueryParameters
{
    /// <summary>Restricts the list to one item group (Products, Fertilizers, Seed, Other).</summary>
    public int? ItemGroupId { get; set; }
    public int? ItemSubGroupId { get; set; }
    public int? CompanyId { get; set; }
    public int? GstSlabId { get; set; }
    public string? Barcode { get; set; }
    public string? RackNumber { get; set; }

    /// <summary>OutOfStock | LowStock | InStock | OverStock. Null returns everything.</summary>
    public string? StockStatus { get; set; }

    /// <summary>True returns only items with at least one batch expiring inside the warning window.</summary>
    public bool? NearExpiryOnly { get; set; }
}
