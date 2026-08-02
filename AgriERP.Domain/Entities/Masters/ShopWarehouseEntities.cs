using AgriERP.Domain.Common;

namespace AgriERP.Domain.Entities.Masters;

/// <summary>
/// A shop/outlet's details. A standalone editable list, kept separate from
/// CompanyProfile (the single invoice-header identity) on purpose - this one is
/// managed from the "Shop & Warehouse" master screen and touches nothing else.
/// </summary>
public class ShopMaster : MasterEntity
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? GstNo { get; set; }
    public string? OwnerName { get; set; }
    public string? MobileNo { get; set; }
    public string? Email { get; set; }

    public State? State { get; set; }
}

/// <summary>
/// A warehouse with an auto-generated code (WH00001...), a name, an address and
/// a list of bins. Standalone: separate from StorageLocations, which drive stock
/// posting - creating a warehouse here does not affect purchase/stock/batches.
/// </summary>
public class WarehouseMaster : MasterEntity
{
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string? Address { get; set; }

    public ICollection<WarehouseBin> Bins { get; set; } = new List<WarehouseBin>();
}

/// <summary>A named bin inside a warehouse. Replaced wholesale when the warehouse is edited.</summary>
public class WarehouseBin
{
    public int WarehouseBinId { get; set; }
    public int WarehouseId { get; set; }
    public string BinName { get; set; } = string.Empty;

    public WarehouseMaster? Warehouse { get; set; }
}
