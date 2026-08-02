using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Masters.Dtos;

/* ============================== Shop Master ============================== */

public class ShopListDto
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? StateName { get; set; }
    public string? GstNo { get; set; }
    public string? OwnerName { get; set; }
    public string? MobileNo { get; set; }
    public bool IsActive { get; set; }
}

public class ShopDto : ShopListDto
{
    public string? Address { get; set; }
    public int? StateId { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveShopRequest
{
    public string ShopName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? GstNo { get; set; }
    public string? OwnerName { get; set; }
    public string? MobileNo { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ShopQueryParameters : QueryParameters
{
    public int? StateId { get; set; }
}

/* ============================== Warehouse Master ============================== */

public class WarehouseListDto
{
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public int BinCount { get; set; }
    public bool IsActive { get; set; }
}

public class WarehouseDto : WarehouseListDto
{
    /// <summary>Bin names, in the order they were added.</summary>
    public List<string> Bins { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveWarehouseRequest
{
    // WarehouseCode is auto-generated (WH00001...) on create and immutable after,
    // so it is not part of the write model.
    public string WarehouseName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public List<string> Bins { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class WarehouseQueryParameters : QueryParameters
{
}
