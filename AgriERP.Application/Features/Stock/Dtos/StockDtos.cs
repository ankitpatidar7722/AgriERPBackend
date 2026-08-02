using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Stock.Dtos;

/* ---------------------------- ledger ---------------------------- */

public class StockLedgerQueryParameters : QueryParameters
{
    public int? ItemId { get; set; }
    public long? BatchId { get; set; }
    public int? LocationId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public StockTransactionTypeId? TransactionType { get; set; }
}

public class StockLedgerLineDto
{
    public long StockTransactionId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string TransactionTypeName { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public decimal InwardQty { get; set; }
    public decimal OutwardQty { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }

    /// <summary>Running balance for the item, in journal order.</summary>
    public decimal RunningBalance { get; set; }

    public string? ReferenceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
}

/* ---------------------------- opening stock ---------------------------- */

public class OpeningStockLineRequest
{
    public int ItemId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Cost per unit. This becomes the batch's landed cost for profit reporting.</summary>
    public decimal PurchaseRate { get; set; }

    public decimal Mrp { get; set; }
    public decimal SellingRate { get; set; }
}

public class OpeningStockRequest
{
    public DateTime EntryDate { get; set; }
    public int? LocationId { get; set; }
    public string? Remarks { get; set; }
    public List<OpeningStockLineRequest> Lines { get; set; } = new();
}

/* ---------------------------- adjustment ---------------------------- */

public class StockAdjustmentLineRequest
{
    /// <summary>An existing batch to count. NULL means a new-stock line - the
    /// batch is created from the fields below (physical verification of stock
    /// the system did not know about).</summary>
    public long? BatchId { get; set; }

    /// <summary>What was actually counted on the shelf.</summary>
    public decimal PhysicalQty { get; set; }

    public string? Reason { get; set; }

    /* ---- New-stock line (used only when BatchId is null) ---- */
    public int? ItemId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal? Rate { get; set; }
    public decimal? Mrp { get; set; }
    public decimal? SellingRate { get; set; }

    /* ---- Warehouse/Bin label (any line) ---- */
    public int? WarehouseId { get; set; }
    public string? BinName { get; set; }
}

public class SaveStockAdjustmentRequest
{
    public DateTime AdjustmentDate { get; set; }
    public StockAdjustmentType AdjustmentType { get; set; } = StockAdjustmentType.Physical;
    public int? LocationId { get; set; }
    public string? Reason { get; set; }
    public string? Remarks { get; set; }
    public List<StockAdjustmentLineRequest> Lines { get; set; } = new();
}

public class StockAdjustmentLineDto
{
    public long AdjustmentDetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public long BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public decimal SystemQty { get; set; }
    public decimal PhysicalQty { get; set; }

    /// <summary>Negative means shrinkage.</summary>
    public decimal DifferenceQty { get; set; }

    public decimal Rate { get; set; }
    public decimal ValueImpact { get; set; }
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public string? Reason { get; set; }
}

public class StockAdjustmentDto
{
    public long AdjustmentId { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public DateTime AdjustmentDate { get; set; }
    public StockAdjustmentType AdjustmentType { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Remarks { get; set; }
    public decimal TotalIncreaseQty { get; set; }
    public decimal TotalDecreaseQty { get; set; }
    public decimal TotalValueImpact { get; set; }
    public DocumentStatus Status { get; set; }
    public DateTime? PostedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<StockAdjustmentLineDto> Lines { get; set; } = Array.Empty<StockAdjustmentLineDto>();
}

public class StockAdjustmentQueryParameters : QueryParameters
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public StockAdjustmentType? AdjustmentType { get; set; }
    public DocumentStatus? Status { get; set; }
    public int? LocationId { get; set; }
}

/* ---------------------------- transfer ---------------------------- */

public class StockTransferLineRequest
{
    public long FromBatchId { get; set; }
    public decimal Quantity { get; set; }
    public string? Remarks { get; set; }
}

public class SaveStockTransferRequest
{
    public DateTime TransferDate { get; set; }
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public string? Remarks { get; set; }
    public List<StockTransferLineRequest> Lines { get; set; } = new();
}

public class StockTransferLineDto
{
    public long TransferDetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public long FromBatchId { get; set; }
    public long? ToBatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal LineValue { get; set; }
}

public class StockTransferDto
{
    public long TransferId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public int FromLocationId { get; set; }
    public string FromLocationName { get; set; } = string.Empty;
    public int ToLocationId { get; set; }
    public string ToLocationName { get; set; } = string.Empty;
    public decimal TotalQty { get; set; }
    public decimal TotalValue { get; set; }
    public DocumentStatus Status { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? Remarks { get; set; }
    public IReadOnlyList<StockTransferLineDto> Lines { get; set; } = Array.Empty<StockTransferLineDto>();
}

public class StockTransferQueryParameters : QueryParameters
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DocumentStatus? Status { get; set; }
    public int? FromLocationId { get; set; }
}
