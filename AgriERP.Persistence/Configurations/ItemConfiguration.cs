using AgriERP.Domain.Entities.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("ItemMaster");
        b.HasKey(x => x.ItemId);

        b.Property(x => x.ItemCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.ItemName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ShortName).HasMaxLength(60);
        b.Property(x => x.TechnicalName).HasMaxLength(200);
        b.Property(x => x.Brand).HasMaxLength(100);
        b.Property(x => x.RackNumber).HasMaxLength(30);
        b.Property(x => x.Barcode).HasMaxLength(50);
        b.Property(x => x.ImagePath).HasMaxLength(300);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.LicenceNumber).HasMaxLength(50);

        b.Property(x => x.PackingSize).AsNullableQuantity();

        b.Property(x => x.PurchaseRate).AsRate();
        b.Property(x => x.SellingRate).AsRate();
        b.Property(x => x.Mrp).AsRate();
        b.Property(x => x.WholesaleRate).AsRate();
        b.Property(x => x.DealerRate).AsRate();
        b.Property(x => x.MinSellingRate).AsRate();

        b.Property(x => x.MinStockLevel).AsQuantity();
        b.Property(x => x.MaxStockLevel).AsQuantity();
        b.Property(x => x.ReorderLevel).AsQuantity();

        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.ItemGroup).WithMany().HasForeignKey(x => x.ItemGroupId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ItemSubGroup).WithMany(c => c.Items).HasForeignKey(x => x.ItemSubGroupId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Company).WithMany(c => c.Items).HasForeignKey(x => x.CompanyId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PackingUnit).WithMany().HasForeignKey(x => x.PackingUnitId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseUnit).WithMany().HasForeignKey(x => x.PurchaseUnitId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.StockUnit).WithMany().HasForeignKey(x => x.StockUnitId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Hsn).WithMany().HasForeignKey(x => x.HsnId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.GstSlab).WithMany().HasForeignKey(x => x.GstSlabId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DefaultLocation).WithMany().HasForeignKey(x => x.DefaultLocationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ItemCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Items_ItemCode");
        // Filtered: most items carry no barcode and NULLs must not collide.
        b.HasIndex(x => x.Barcode).IsUnique()
         .HasFilter("[IsDeleted] = 0 AND [Barcode] IS NOT NULL")
         .HasDatabaseName("UQ_Items_Barcode");
        // Same brand in two pack sizes is two items; same brand, same pack,
        // entered twice is a duplicate. This catches the second case.
        b.HasIndex(x => new { x.ItemName, x.CompanyId, x.PackingSize, x.PackingUnitId })
         .IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Items_Name_Company_Packing");
    }
}

public class ItemBatchConfiguration : IEntityTypeConfiguration<ItemBatch>
{
    public void Configure(EntityTypeBuilder<ItemBatch> b)
    {
        b.ToTable("ItemBatches");
        b.HasKey(x => x.BatchId);

        b.Property(x => x.BatchNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.ManufacturingDate).AsNullableDate();
        b.Property(x => x.ExpiryDate).AsNullableDate();

        b.Property(x => x.PurchaseRate).AsRate();
        b.Property(x => x.Mrp).AsRate();
        b.Property(x => x.SellingRate).AsRate();

        b.Property(x => x.InwardQty).AsQuantity();
        b.Property(x => x.OutwardQty).AsQuantity();
        // PERSISTED: derived, indexable, and unable to drift from its inputs.
        b.Property(x => x.CurrentQty).AsQuantity().AsComputed("[InwardQty]-[OutwardQty]");

        b.Property(x => x.Remarks).HasMaxLength(300);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        // Convenience properties evaluated in C#, not columns.
        b.Ignore(x => x.IsExpired);
        b.Ignore(x => x.DaysToExpiry);

        b.HasOne(x => x.Item).WithMany(p => p.Batches).HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);

        // One physical lot per item+batch+location. Re-purchasing the same
        // batch adds to this row rather than creating a second one.
        b.HasIndex(x => new { x.ItemId, x.BatchNumber, x.LocationId }).IsUnique()
         .HasDatabaseName("UQ_ItemBatches_Item_Batch_Location");
        // FEFO picking.
        b.HasIndex(x => new { x.ItemId, x.ExpiryDate })
         .HasDatabaseName("IX_ItemBatches_Item_Expiry");
    }
}

public class ItemImageConfiguration : IEntityTypeConfiguration<ItemImage>
{
    public void Configure(EntityTypeBuilder<ItemImage> b)
    {
        b.ToTable("ItemImages");
        b.HasKey(x => x.ItemImageId);

        b.Property(x => x.FilePath).HasMaxLength(300).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.CreatedAt).AsCreatedAt();

        b.HasOne(x => x.Item).WithMany(p => p.Images).HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.ItemId).IsUnique().HasFilter("[IsPrimary] = 1")
         .HasDatabaseName("UQ_ItemImages_Primary");
    }
}

public class ItemPriceHistoryConfiguration : IEntityTypeConfiguration<ItemPriceHistory>
{
    public void Configure(EntityTypeBuilder<ItemPriceHistory> b)
    {
        b.ToTable("ItemPriceHistory");
        b.HasKey(x => x.PriceHistoryId);

        b.Property(x => x.ChangedAt).AsCreatedAt();
        b.Property(x => x.ChangeSource).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ReferenceNumber).HasMaxLength(30);
        b.Property(x => x.Remarks).HasMaxLength(300);

        b.Property(x => x.OldPurchaseRate).AsNullableRate();
        b.Property(x => x.NewPurchaseRate).AsNullableRate();
        b.Property(x => x.OldSellingRate).AsNullableRate();
        b.Property(x => x.NewSellingRate).AsNullableRate();
        b.Property(x => x.OldMrp).AsNullableRate();
        b.Property(x => x.NewMrp).AsNullableRate();

        b.HasOne(x => x.Item).WithMany(p => p.PriceHistory).HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ItemId, x.ChangedAt })
         .HasDatabaseName("IX_ItemPriceHistory_Item_Date");
    }
}
