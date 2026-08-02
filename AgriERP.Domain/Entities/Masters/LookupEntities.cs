using AgriERP.Domain.Common;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.Masters;

/// <summary>
/// Maps to States. StateId IS the numeric GST state code (27 = Maharashtra),
/// which is why it is not an identity column. Comparing the shop's state to the
/// party's is what decides CGST+SGST versus IGST on every document.
/// </summary>
public class State
{
    public int StateId { get; set; }
    public string StateCode { get; set; } = string.Empty;   // '27'
    public string StateName { get; set; } = string.Empty;
    public string? StateAbbr { get; set; }                  // 'MH'
    public bool IsUnionTerritory { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Maps to Units.</summary>
public class Unit : MasterEntity
{
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;    // KG, LTR, BTL
    public string UnitName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Seeds sell in half kilos; bags of urea do not sell in fractions.</summary>
    public bool AllowDecimal { get; set; } = true;

    public int DisplayOrder { get; set; }
}

/// <summary>
/// Maps to GstSlabs. Both the intra-state split (CGST+SGST) and the
/// inter-state rate (IGST) are stored, because a document records what was
/// actually charged.
/// </summary>
public class GstSlab
{
    public int GstSlabId { get; set; }
    public string SlabName { get; set; } = string.Empty;    // 'GST 18%'
    public decimal TotalRate { get; set; }                  // 18.000
    public decimal CgstRate { get; set; }                   //  9.000
    public decimal SgstRate { get; set; }                   //  9.000
    public decimal IgstRate { get; set; }                   // 18.000
    public decimal CessRate { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Maps to HsnCodes.</summary>
public class HsnCode
{
    public int HsnId { get; set; }
    public string Code { get; set; } = string.Empty;        // column: HsnCode
    public string Description { get; set; } = string.Empty;
    public int? DefaultGstSlabId { get; set; }
    public bool IsActive { get; set; } = true;

    public GstSlab? DefaultGstSlab { get; set; }
}

/// <summary>
/// Maps to VoucherMaster - the Indus-style document-type registry.
///
/// One transaction id already ties a document's header to its lines
/// (Purchase.PurchaseId, PurchaseOrder.PurchaseOrderId). VoucherId says WHICH
/// document it is - a Purchase Order (booking) or a Purchase GRN (goods
/// received, stock in) - so the same purchase tables carry both, distinguished
/// by voucher, exactly the way ItemTransactionMain does in Indus.
/// </summary>
public class VoucherMaster
{
    public int VoucherId { get; set; }

    /// <summary>Stable key the code refers to; never renumbered. 'PO', 'PGRN'.</summary>
    public string VoucherCode { get; set; } = string.Empty;

    /// <summary>Shown to users: 'Purchase Order', 'Purchase GRN'.</summary>
    public string VoucherName { get; set; } = string.Empty;

    /// <summary>
    /// Family the document belongs to, so one query can pull "all purchase
    /// vouchers" without hard-coding ids. 'PurchaseOrder', 'Purchase', 'Sales'.
    /// </summary>
    public string VoucherType { get; set; } = string.Empty;

    /// <summary>Document-number prefix: 'PO', 'GRN'.</summary>
    public string Prefix { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Maps to StorageLocations. Self-referencing so a rack can sit inside a
/// godown. Stock transfers move batches between these.
/// </summary>
public class StorageLocation : MasterEntity
{
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public StorageLocationType LocationType { get; set; } = StorageLocationType.Rack;
    public int? ParentLocationId { get; set; }

    /// <summary>Exactly one location carries this; stock entries omitting a location land here.</summary>
    public bool IsDefault { get; set; }

    public string? Remarks { get; set; }

    public StorageLocation? ParentLocation { get; set; }
    public ICollection<StorageLocation> ChildLocations { get; set; } = new List<StorageLocation>();
}
