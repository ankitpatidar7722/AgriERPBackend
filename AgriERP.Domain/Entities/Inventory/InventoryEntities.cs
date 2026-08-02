using AgriERP.Domain.Common;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.Inventory;

/// <summary>
/// Maps to TransactionTypes. A closed lookup; ids are fixed.
///
/// The key is the enum rather than a raw byte. Besides reading better, EF
/// requires a foreign key and the principal key it targets to share a CLR
/// type, and StockTransaction.TransactionTypeId is the enum.
/// </summary>
public class TransactionType
{
    public StockTransactionTypeId TransactionTypeId { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;

    /// <summary>+1 inward, -1 outward.</summary>
    public short Direction { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Maps to StockTransactions - the append-only movement journal.
///
/// Nothing ever updates or deletes a row here. Cancelling a document appends a
/// reversing row linked by <see cref="ReversesTransactionId"/>. That is what
/// makes closing stock for any past date reproducible years later.
///
/// Write to this table ONLY through usp_PostStockTransaction, which
/// validates the movement and updates the batch totals in the same transaction.
/// </summary>
public class StockTransaction
{
    public long StockTransactionId { get; set; }
    public DateTime TransactionDate { get; set; }
    public StockTransactionTypeId TransactionTypeId { get; set; }

    public int ItemId { get; set; }
    public long BatchId { get; set; }
    public int LocationId { get; set; }

    /// <summary>
    /// +1 or -1, duplicated from TransactionTypes because a PERSISTED computed
    /// column cannot read another table and SignedQuantity must be persisted
    /// for date-range stock sums to stay an index scan.
    /// </summary>
    public short Direction { get; set; }

    /// <summary>Always positive. The sign lives in <see cref="Direction"/>.</summary>
    public decimal Quantity { get; set; }

    /// <summary>PERSISTED computed column: Quantity * Direction.</summary>
    public decimal SignedQuantity { get; private set; }

    public decimal Rate { get; set; }

    /// <summary>PERSISTED computed column: Quantity * Rate.</summary>
    public decimal Value { get; private set; }

    // Polymorphic link to the source document - deliberately not a foreign key,
    // since one journal serves six document types.
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public long? ReferenceDetailId { get; set; }
    public string? ReferenceNumber { get; set; }

    /// <summary>Set on the reversing row when a document is cancelled.</summary>
    public long? ReversesTransactionId { get; set; }

    public string? Remarks { get; set; }
    public int? FinancialYearId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }

    public TransactionType? Type { get; set; }
    public Item? Item { get; set; }
    public ItemBatch? Batch { get; set; }
    public StorageLocation? Location { get; set; }
}

/// <summary>
/// Maps to StockAdjustments. Covers physical-count differences, damage,
/// expiry write-offs, and the initial opening-stock load on migration.
/// </summary>
public class StockAdjustment : DocumentEntity
{
    public long AdjustmentId { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public DateTime AdjustmentDate { get; set; }
    public StockAdjustmentType AdjustmentType { get; set; }
    public int LocationId { get; set; }
    public string? Reason { get; set; }
    public string? Remarks { get; set; }

    public decimal TotalIncreaseQty { get; set; }
    public decimal TotalDecreaseQty { get; set; }
    public decimal TotalValueImpact { get; set; }

    /// <summary>Draft touches no stock. Posting is what writes the journal.</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }
    public int? ApprovedBy { get; set; }

    public StorageLocation? Location { get; set; }
    public ICollection<StockAdjustmentDetail> Details { get; set; } = new List<StockAdjustmentDetail>();
}

/// <summary>Maps to StockAdjustmentDetails.</summary>
public class StockAdjustmentDetail
{
    public long AdjustmentDetailId { get; set; }
    public long AdjustmentId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public long BatchId { get; set; }

    public decimal SystemQty { get; set; }
    public decimal PhysicalQty { get; set; }

    /// <summary>PERSISTED computed column. Negative means shrinkage.</summary>
    public decimal DifferenceQty { get; private set; }

    public decimal Rate { get; set; }

    /// <summary>PERSISTED computed column: (PhysicalQty - SystemQty) * Rate.</summary>
    public decimal ValueImpact { get; private set; }

    /// <summary>Organisational label only (like the GRN warehouse); stock still
    /// posts to LocationId. Where the counted goods physically sit.</summary>
    public int? WarehouseId { get; set; }
    public string? BinName { get; set; }

    public string? Reason { get; set; }

    public StockAdjustment? Adjustment { get; set; }
    public Item? Item { get; set; }
    public ItemBatch? Batch { get; set; }
    public AgriERP.Domain.Entities.Masters.WarehouseMaster? Warehouse { get; set; }
}

/// <summary>Maps to StockTransfers - moving batches between godown and counter.</summary>
public class StockTransfer : DocumentEntity
{
    public long TransferId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalValue { get; set; }
    public string? Remarks { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTime? PostedAt { get; set; }
    public int? PostedBy { get; set; }

    public StorageLocation? FromLocation { get; set; }
    public StorageLocation? ToLocation { get; set; }
    public ICollection<StockTransferDetail> Details { get; set; } = new List<StockTransferDetail>();
}

/// <summary>Maps to StockTransferDetails.</summary>
public class StockTransferDetail
{
    public long TransferDetailId { get; set; }
    public long TransferId { get; set; }
    public int LineNumber { get; set; }
    public int ItemId { get; set; }

    /// <summary>Source batch at the FROM location.</summary>
    public long FromBatchId { get; set; }

    /// <summary>Matching batch row at the TO location, created on posting if absent.</summary>
    public long? ToBatchId { get; set; }

    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }

    /// <summary>PERSISTED computed column: Quantity * Rate.</summary>
    public decimal LineValue { get; private set; }

    public string? Remarks { get; set; }

    public StockTransfer? Transfer { get; set; }
    public Item? Item { get; set; }
    public ItemBatch? FromBatch { get; set; }
    public ItemBatch? ToBatch { get; set; }
}
