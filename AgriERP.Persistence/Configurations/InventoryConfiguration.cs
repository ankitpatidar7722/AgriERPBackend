using AgriERP.Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

public class TransactionTypeConfiguration : IEntityTypeConfiguration<TransactionType>
{
    public void Configure(EntityTypeBuilder<TransactionType> b)
    {
        b.ToTable("TransactionTypes");
        b.HasKey(x => x.TransactionTypeId);

        // Ids are a fixed enum referenced by stored procedures - never generated.
        b.Property(x => x.TransactionTypeId).HasColumnType("tinyint").ValueGeneratedNever();
        b.Property(x => x.TypeCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.TypeName).HasMaxLength(60).IsRequired();

        b.HasIndex(x => x.TypeCode).IsUnique().HasDatabaseName("UQ_TransactionTypes_Code");
    }
}

public class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
{
    public void Configure(EntityTypeBuilder<StockTransaction> b)
    {
        b.ToTable("StockTransactions");
        b.HasKey(x => x.StockTransactionId);

        b.Property(x => x.TransactionDate).AsTimestamp();
        // Enum backed by TINYINT ids seeded in 05_Inventory.sql.
        b.Property(x => x.TransactionTypeId).HasColumnType("tinyint");

        b.Property(x => x.Quantity).AsQuantity();
        b.Property(x => x.SignedQuantity).AsQuantity().AsComputed("[Quantity]*[Direction]");
        b.Property(x => x.Rate).AsRate();
        b.Property(x => x.Value).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[Quantity]*[Rate])");

        b.Property(x => x.ReferenceType).HasMaxLength(30);
        b.Property(x => x.ReferenceNumber).HasMaxLength(30);
        b.Property(x => x.Remarks).HasMaxLength(300);
        b.Property(x => x.CreatedAt).AsCreatedAt();

        b.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TransactionTypeId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ItemId, x.TransactionDate, x.StockTransactionId })
         .HasDatabaseName("IX_StockTransactions_Item_Date");
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
         .HasDatabaseName("IX_StockTransactions_Reference");
    }
}

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("StockAdjustments");
        b.HasKey(x => x.AdjustmentId);

        b.Property(x => x.AdjustmentNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.AdjustmentDate).AsDate();
        b.Property(x => x.AdjustmentType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.Remarks).HasMaxLength(500);

        b.Property(x => x.TotalIncreaseQty).AsQuantity();
        b.Property(x => x.TotalDecreaseQty).AsQuantity();
        b.Property(x => x.TotalValueImpact).AsAmount();

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.PostedAt).AsNullableTimestamp();
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.AdjustmentNumber).IsUnique().HasDatabaseName("UQ_StockAdjustments_Number");
    }
}

public class StockAdjustmentDetailConfiguration : IEntityTypeConfiguration<StockAdjustmentDetail>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentDetail> b)
    {
        b.ToTable("StockAdjustmentDetails");
        b.HasKey(x => x.AdjustmentDetailId);

        b.Property(x => x.SystemQty).AsQuantity();
        b.Property(x => x.PhysicalQty).AsQuantity();
        b.Property(x => x.DifferenceQty).AsQuantity().AsComputed("[PhysicalQty]-[SystemQty]");
        b.Property(x => x.Rate).AsRate();
        b.Property(x => x.ValueImpact).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),([PhysicalQty]-[SystemQty])*[Rate])");
        b.Property(x => x.BinName).HasMaxLength(50);
        b.Property(x => x.Reason).HasMaxLength(300);

        b.HasOne(x => x.Adjustment).WithMany(a => a.Details).HasForeignKey(x => x.AdjustmentId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
         .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.AdjustmentId, x.LineNumber })
         .HasDatabaseName("IX_StockAdjustmentDetails_AdjustmentId");
    }
}

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("StockTransfers");
        b.HasKey(x => x.TransferId);

        b.Property(x => x.TransferNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.TransferDate).AsDate();
        b.Property(x => x.TotalQty).AsQuantity();
        b.Property(x => x.TotalValue).AsAmount();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.PostedAt).AsNullableTimestamp();
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.TransferNumber).IsUnique().HasDatabaseName("UQ_StockTransfers_Number");
    }
}

public class StockTransferDetailConfiguration : IEntityTypeConfiguration<StockTransferDetail>
{
    public void Configure(EntityTypeBuilder<StockTransferDetail> b)
    {
        b.ToTable("StockTransferDetails");
        b.HasKey(x => x.TransferDetailId);

        b.Property(x => x.Quantity).AsQuantity();
        b.Property(x => x.Rate).AsRate();
        b.Property(x => x.LineValue).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[Quantity]*[Rate])");
        b.Property(x => x.Remarks).HasMaxLength(300);

        b.HasOne(x => x.Transfer).WithMany(t => t.Details).HasForeignKey(x => x.TransferId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.FromBatch).WithMany().HasForeignKey(x => x.FromBatchId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ToBatch).WithMany().HasForeignKey(x => x.ToBatchId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TransferId, x.LineNumber })
         .HasDatabaseName("IX_StockTransferDetails_TransferId");
    }
}
