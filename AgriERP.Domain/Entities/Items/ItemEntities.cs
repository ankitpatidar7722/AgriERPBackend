using AgriERP.Domain.Common;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.Items;

/// <summary>
/// Maps to ItemMaster.
///
/// Note what is NOT here: BatchNumber, ManufacturingDate, ExpiryDate and
/// CurrentStock. An item is bought many times, each consignment with its own
/// batch, expiry and cost, so those live on <see cref="ItemBatch"/>. Current
/// stock is the sum across batches and is read from vw_ItemStock.
/// </summary>
public class Item : MasterEntity
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;     // PRD-000001
    public string ItemName { get; set; } = string.Empty;     // 'Confidor 17.8% SL'
    public string? ShortName { get; set; }                      // printed on the invoice line

    /// <summary>
    /// Active ingredient, e.g. "Imidacloprid 17.8% SL". Farmers ask for the
    /// technical name as often as the brand, so it is indexed and searchable.
    /// </summary>
    public string? TechnicalName { get; set; }

    /// <summary>
    /// Which kind of item this is - Product, Fertilizer, Seed or Other.
    ///
    /// It decides which fields the entry form shows, and it owns a separate
    /// code series, so a seed is SED-000001 while a pesticide is PRD-000001.
    /// It is denormalised from the sub-group deliberately: every stock, sales
    /// and purchase query filters by group, and following the sub-group for
    /// that would put a join on the hot path of the billing screen.
    /// </summary>
    public int ItemGroupId { get; set; }
    public ItemGroup? ItemGroup { get; set; }

    /// <summary>
    /// Values for this item's group-specific fields - the ones with no column
    /// of their own, such as seed germination or fertilizer N-P-K.
    /// </summary>
    public ICollection<ItemMasterDetail> ExtraFieldValues { get; set; } = new List<ItemMasterDetail>();

    public int ItemSubGroupId { get; set; }
    public int? CompanyId { get; set; }
    public string? Brand { get; set; }

    /// <summary>250 + ML renders as "250 ML" on screen and in reports.</summary>
    public decimal? PackingSize { get; set; }
    public int? PackingUnitId { get; set; }

    /// <summary>The unit the item is SOLD in (bottle, packet, bag).</summary>
    public int UnitId { get; set; }

    /// <summary>The unit the item is BOUGHT in. Optional; the selling unit is used when unset.</summary>
    public int? PurchaseUnitId { get; set; }

    /// <summary>The unit the item is STOCKED/counted in. Optional; the selling unit is used when unset.</summary>
    public int? StockUnitId { get; set; }

    public int? HsnId { get; set; }
    public int GstSlabId { get; set; }

    /// <summary>True when the rates below already contain GST, as with MRP-billed items.</summary>
    public bool IsRateInclusiveOfTax { get; set; }

    // Pricing. DECIMAL(18,4): agri rates carry paise-level precision.
    public decimal PurchaseRate { get; set; }
    public decimal SellingRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal WholesaleRate { get; set; }
    public decimal DealerRate { get; set; }

    /// <summary>Floor price. Going below needs the Sales.OverrideMinRate permission.</summary>
    public decimal MinSellingRate { get; set; }

    // Stock policy. Quantities are DECIMAL(18,3).
    public decimal MinStockLevel { get; set; }
    public decimal MaxStockLevel { get; set; }
    public decimal ReorderLevel { get; set; }

    public bool IsBatchTracked { get; set; } = true;
    public bool IsExpiryTracked { get; set; } = true;

    /// <summary>Off by default: selling stock you do not have corrupts valuation.</summary>
    public bool AllowNegativeStock { get; set; }

    public int? DefaultLocationId { get; set; }
    public string? RackNumber { get; set; }
    public string? Barcode { get; set; }

    public string? ImagePath { get; set; }
    public string? Description { get; set; }

    /// <summary>CIB registration / pesticide licence number, kept for inspections.</summary>
    public string? LicenceNumber { get; set; }

    public ItemSubGroup? ItemSubGroup { get; set; }
    public Company? Company { get; set; }
    public Unit? Unit { get; set; }
    public Unit? PackingUnit { get; set; }
    public Unit? PurchaseUnit { get; set; }
    public Unit? StockUnit { get; set; }
    public HsnCode? Hsn { get; set; }
    public GstSlab? GstSlab { get; set; }
    public StorageLocation? DefaultLocation { get; set; }

    public ICollection<ItemBatch> Batches { get; set; } = new List<ItemBatch>();
    public ICollection<ItemImage> Images { get; set; } = new List<ItemImage>();
    public ICollection<ItemPriceHistory> PriceHistory { get; set; } = new List<ItemPriceHistory>();
}

/// <summary>
/// Maps to ItemBatches - the single source of truth for on-hand stock.
///
/// One row per (item, batch number, location). Items with no real
/// batches still get one row with BatchNumber 'GEN', so every stock path in
/// the application is identical and billing needs no "is it batched?" branch.
/// </summary>
public class ItemBatch : AuditableEntity, IHasRowVersion
{
    public long BatchId { get; set; }
    public int ItemId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Landed cost for THIS batch. Batch-wise profit and valuation read it.</summary>
    public decimal PurchaseRate { get; set; }

    public decimal Mrp { get; set; }
    public decimal SellingRate { get; set; }

    /// <summary>Maintained only by usp_PostStockTransaction. Never assign directly.</summary>
    public decimal InwardQty { get; set; }

    /// <summary>Maintained only by usp_PostStockTransaction. Never assign directly.</summary>
    public decimal OutwardQty { get; set; }

    /// <summary>
    /// PERSISTED computed column (InwardQty - OutwardQty). Indexable, and
    /// structurally unable to disagree with its own inputs.
    /// </summary>
    public decimal CurrentQty { get; private set; }

    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }

    public Item? Item { get; set; }
    public StorageLocation? Location { get; set; }

    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value.Date < DateTime.Today;
    public int? DaysToExpiry => ExpiryDate.HasValue
        ? (int)(ExpiryDate.Value.Date - DateTime.Today).TotalDays
        : null;
}

/// <summary>
/// Maps to ItemImages. Only the path is stored - image bytes in the
/// database bloat every backup and slow every restore.
/// </summary>
public class ItemImage
{
    public long ItemImageId { get; set; }
    public int ItemId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }

    public Item? Item { get; set; }
}

/// <summary>Maps to ItemPriceHistory.</summary>
public class ItemPriceHistory
{
    public long PriceHistoryId { get; set; }
    public int ItemId { get; set; }
    public DateTime ChangedAt { get; set; }
    public int? ChangedBy { get; set; }

    /// <summary>Manual when edited on the item screen, Purchase when a bill pushed it through.</summary>
    public PriceChangeSource ChangeSource { get; set; } = PriceChangeSource.Manual;

    public string? ReferenceNumber { get; set; }

    public decimal? OldPurchaseRate { get; set; }
    public decimal? NewPurchaseRate { get; set; }
    public decimal? OldSellingRate { get; set; }
    public decimal? NewSellingRate { get; set; }
    public decimal? OldMrp { get; set; }
    public decimal? NewMrp { get; set; }
    public string? Remarks { get; set; }

    public Item? Item { get; set; }
}
