using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Purchases.Dtos;

public class PurchaseRequisitionLineRequest
{
    public int ItemId { get; set; }
    public decimal RequiredQty { get; set; }
    public int? UnitId { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public decimal EstimatedRate { get; set; }
    public string? Remarks { get; set; }
}

public class SavePurchaseRequisitionRequest
{
    public DateTime RequisitionDate { get; set; }
    public int? LocationId { get; set; }
    public string? Remarks { get; set; }
    public List<PurchaseRequisitionLineRequest> Lines { get; set; } = new();
}

public class PurchaseRequisitionLineDto
{
    public long RequisitionDetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemGroupName { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal RequiredQty { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal PendingQty { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public decimal EstimatedRate { get; set; }
    public string? Remarks { get; set; }

    // Carried from the item master so a purchase order raised from this
    // requisition can open each line with the right tax and prices already set.
    public decimal GstPercent { get; set; }
    public string? HsnCode { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingRate { get; set; }
}

public class PurchaseRequisitionDto
{
    public long RequisitionId { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateTime RequisitionDate { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public decimal TotalQty { get; set; }
    public PurchaseRequisitionStatus Status { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Item names on the requisition, for the list grid (the detail has full Lines).</summary>
    public IReadOnlyList<string> ItemNames { get; set; } = Array.Empty<string>();

    public IReadOnlyList<PurchaseRequisitionLineDto> Lines { get; set; } = Array.Empty<PurchaseRequisitionLineDto>();
}

/// <summary>
/// One requisition LINE flattened with its parent's header, for the item-wise
/// requisition list: every item is its own row, each carrying its RequisitionId.
/// </summary>
public class PurchaseRequisitionItemRowDto
{
    public long RequisitionId { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateTime RequisitionDate { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public PurchaseRequisitionStatus Status { get; set; }

    public long RequisitionDetailId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemGroupName { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public decimal RequiredQty { get; set; }
    public decimal PendingQty { get; set; }
    public decimal EstimatedRate { get; set; }
    public decimal EstimatedAmount { get; set; }
}

public class PurchaseRequisitionQueryParameters : QueryParameters
{
    public PurchaseRequisitionStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>True returns only requisitions still awaiting a purchase order (Open or Partial).</summary>
    public bool? PendingOnly { get; set; }
}
