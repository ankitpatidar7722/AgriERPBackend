using AgriERP.Domain.Entities.Purchases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

// The GrandTotal / BalanceAmount / PaymentStatus expressions below are repeated
// verbatim from 06_Purchase.sql. SQL Server has no macro for a computed column,
// so the full expression is spelled out in both places - if one changes, change
// both in the same commit.
internal static class PurchaseComputedSql
{
    public const string GrandTotal =
        "[TaxableAmount]+[CgstAmount]+[SgstAmount]+[IgstAmount]+[CessAmount]" +
        "+[FreightCharges]+[OtherCharges]+[RoundOff]";

    public const string BalanceAmount = GrandTotal + "-[PaidAmount]";

    public const string PaymentStatus =
        "case when [PaidAmount]<=(0) then N'Unpaid' " +
        "when [PaidAmount]>=(" + GrandTotal + ") then N'Paid' " +
        "else N'Partial' end";
}

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("PurchaseOrders");
        b.HasKey(x => x.PurchaseOrderId);

        b.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.OrderDate).AsDate();
        b.Property(x => x.ExpectedDate).AsNullableDate();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.TotalQty).AsQuantity();
        b.Property(x => x.EstimatedValue).AsAmount();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.OrderNumber).IsUnique().HasDatabaseName("UQ_PurchaseOrders_Number");
    }
}

public class PurchaseOrderDetailConfiguration : IEntityTypeConfiguration<PurchaseOrderDetail>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderDetail> b)
    {
        b.ToTable("PurchaseOrderDetails");
        b.HasKey(x => x.PurchaseOrderDetailId);

        b.Property(x => x.OrderedQty).AsQuantity();
        b.Property(x => x.ReceivedQty).AsQuantity();
        b.Property(x => x.PendingQty).AsQuantity().AsComputed("[OrderedQty]-[ReceivedQty]");
        b.Property(x => x.Rate).AsRate();
        b.Property(x => x.EstimatedAmount).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[OrderedQty]*[Rate])");
        b.Property(x => x.NoOfPacks).AsQuantity();
        b.Property(x => x.QtyPerPack).AsQuantity();
        b.Property(x => x.RequiredQty).AsNullableQuantity();
        b.Property(x => x.Remarks).HasMaxLength(300);
        b.Property(x => x.ItemRemark).HasMaxLength(300);

        b.HasOne(x => x.PurchaseOrder).WithMany(o => o.Details).HasForeignKey(x => x.PurchaseOrderId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RequisitionDetail).WithMany().HasForeignKey(x => x.RequisitionDetailId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.PurchaseOrderId, x.LineNumber })
         .HasDatabaseName("IX_PurchaseOrderDetails_OrderId");
    }
}

public class PurchaseRequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> b)
    {
        b.ToTable("PurchaseRequisitions");
        b.HasKey(x => x.RequisitionId);

        b.Property(x => x.RequisitionNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.RequisitionDate).AsDate();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.TotalQty).AsQuantity();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.RequisitionNumber).IsUnique().HasDatabaseName("UQ_PurchaseRequisitions_Number");
    }
}

public class PurchaseRequisitionDetailConfiguration : IEntityTypeConfiguration<PurchaseRequisitionDetail>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisitionDetail> b)
    {
        b.ToTable("PurchaseRequisitionDetails");
        b.HasKey(x => x.RequisitionDetailId);

        b.Property(x => x.RequiredQty).AsQuantity();
        b.Property(x => x.OrderedQty).AsQuantity();
        b.Property(x => x.PendingQty).AsQuantity().AsComputed("[RequiredQty]-[OrderedQty]");
        b.Property(x => x.ExpectedDate).AsNullableDate();
        b.Property(x => x.EstimatedRate).AsRate();
        b.Property(x => x.Remarks).HasMaxLength(300);

        b.HasOne(x => x.Requisition).WithMany(r => r.Details).HasForeignKey(x => x.RequisitionId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.RequisitionId, x.LineNumber })
         .HasDatabaseName("IX_PurchaseRequisitionDetails_ReqId");
    }
}

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        b.ToTable("Purchases");
        b.HasKey(x => x.PurchaseId);

        b.Property(x => x.PurchaseNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.PurchaseDate).AsDate();
        b.Property(x => x.SupplierInvoiceNumber).HasMaxLength(50);
        b.Property(x => x.SupplierInvoiceDate).AsNullableDate();
        b.Property(x => x.DueDate).AsNullableDate();

        b.Property(x => x.GrossAmount).AsAmount();
        b.Property(x => x.DiscountAmount).AsAmount();
        b.Property(x => x.TaxableAmount).AsAmount();
        b.Property(x => x.CgstAmount).AsAmount();
        b.Property(x => x.SgstAmount).AsAmount();
        b.Property(x => x.IgstAmount).AsAmount();
        b.Property(x => x.CessAmount).AsAmount();
        b.Property(x => x.FreightCharges).AsAmount();
        b.Property(x => x.OtherCharges).AsAmount();
        b.Property(x => x.RoundOff).AsAmount();
        b.Property(x => x.PaidAmount).AsAmount();

        b.Property(x => x.GrandTotal).AsAmount().AsComputed(PurchaseComputedSql.GrandTotal);
        b.Property(x => x.BalanceAmount).AsAmount().AsComputed(PurchaseComputedSql.BalanceAmount);
        b.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(7)
         .AsComputed(PurchaseComputedSql.PaymentStatus);

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.PostedAt).AsNullableTimestamp();
        b.Property(x => x.CancelledAt).AsNullableTimestamp();
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SupplierState).WithMany().HasForeignKey(x => x.SupplierStateId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.PurchaseNumber).IsUnique().HasDatabaseName("UQ_Purchases_PurchaseNumber");
        // Blocks the same supplier bill being entered twice.
        b.HasIndex(x => new { x.SupplierId, x.SupplierInvoiceNumber }).IsUnique()
         .HasFilter("[SupplierInvoiceNumber] IS NOT NULL AND [Status]<>'Cancelled'")
         .HasDatabaseName("UQ_Purchases_Supplier_InvoiceNumber");
    }
}

public class PurchaseDetailConfiguration : IEntityTypeConfiguration<PurchaseDetail>
{
    public void Configure(EntityTypeBuilder<PurchaseDetail> b)
    {
        b.ToTable("PurchaseDetails");
        b.HasKey(x => x.PurchaseDetailId);

        b.Property(x => x.BatchNumber).HasMaxLength(50);
        b.Property(x => x.ManufacturingDate).AsNullableDate();
        b.Property(x => x.ExpiryDate).AsNullableDate();

        b.Property(x => x.Quantity).AsQuantity();
        b.Property(x => x.FreeQuantity).AsQuantity();
        b.Property(x => x.TotalQuantity).AsQuantity().AsComputed("[Quantity]+[FreeQuantity]");

        b.Property(x => x.Rate).AsRate();
        b.Property(x => x.GrossAmount).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[Quantity]*[Rate])");
        b.Property(x => x.DiscountPercent).AsPercent();
        b.Property(x => x.DiscountAmount).AsAmount();
        b.Property(x => x.TaxableAmount).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[Quantity]*[Rate])-[DiscountAmount]");

        b.Property(x => x.GstPercent).AsPercent();
        b.Property(x => x.CgstAmount).AsAmount();
        b.Property(x => x.SgstAmount).AsAmount();
        b.Property(x => x.IgstAmount).AsAmount();
        b.Property(x => x.CessAmount).AsAmount();
        b.Property(x => x.LineTotal).AsAmount().AsComputed(
            "CONVERT([decimal](18,2),[Quantity]*[Rate])-[DiscountAmount]" +
            "+[CgstAmount]+[SgstAmount]+[IgstAmount]+[CessAmount]");

        b.Property(x => x.LandedRate).AsRate();
        b.Property(x => x.Mrp).AsRate();
        b.Property(x => x.SellingRate).AsRate();
        b.Property(x => x.HsnCode).HasMaxLength(10);
        b.Property(x => x.Remarks).HasMaxLength(300);

        b.HasOne(x => x.Purchase).WithMany(p => p.Details).HasForeignKey(x => x.PurchaseId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.PurchaseId, x.LineNumber })
         .HasDatabaseName("IX_PurchaseDetails_PurchaseId");
        b.HasIndex(x => x.ItemId).HasDatabaseName("IX_PurchaseDetails_ItemId");
    }
}

public class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> b)
    {
        b.ToTable("PurchaseReturns");
        b.HasKey(x => x.PurchaseReturnId);

        b.Property(x => x.ReturnNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.ReturnDate).AsDate();
        b.Property(x => x.DebitNoteNumber).HasMaxLength(50);
        b.Property(x => x.ReturnReason).HasMaxLength(300);

        b.Property(x => x.GrossAmount).AsAmount();
        b.Property(x => x.DiscountAmount).AsAmount();
        b.Property(x => x.TaxableAmount).AsAmount();
        b.Property(x => x.CgstAmount).AsAmount();
        b.Property(x => x.SgstAmount).AsAmount();
        b.Property(x => x.IgstAmount).AsAmount();
        b.Property(x => x.CessAmount).AsAmount();
        b.Property(x => x.RoundOff).AsAmount();
        b.Property(x => x.GrandTotal).AsAmount().AsComputed(
            "[TaxableAmount]+[CgstAmount]+[SgstAmount]+[IgstAmount]+[CessAmount]+[RoundOff]");

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.PostedAt).AsNullableTimestamp();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Purchase).WithMany().HasForeignKey(x => x.PurchaseId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ReturnNumber).IsUnique().HasDatabaseName("UQ_PurchaseReturns_Number");
    }
}

public class PurchaseReturnDetailConfiguration : IEntityTypeConfiguration<PurchaseReturnDetail>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnDetail> b)
    {
        b.ToTable("PurchaseReturnDetails");
        b.HasKey(x => x.PurchaseReturnDetailId);

        b.Property(x => x.Quantity).AsQuantity();
        b.Property(x => x.Rate).AsRate();
        b.Property(x => x.GrossAmount).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[Quantity]*[Rate])");
        b.Property(x => x.DiscountAmount).AsAmount();
        b.Property(x => x.TaxableAmount).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[Quantity]*[Rate])-[DiscountAmount]");
        b.Property(x => x.GstPercent).AsPercent();
        b.Property(x => x.CgstAmount).AsAmount();
        b.Property(x => x.SgstAmount).AsAmount();
        b.Property(x => x.IgstAmount).AsAmount();
        b.Property(x => x.CessAmount).AsAmount();
        b.Property(x => x.LineTotal).AsAmount().AsComputed(
            "CONVERT([decimal](18,2),[Quantity]*[Rate])-[DiscountAmount]" +
            "+[CgstAmount]+[SgstAmount]+[IgstAmount]+[CessAmount]");
        b.Property(x => x.ReturnReason).HasMaxLength(300);

        b.HasOne(x => x.PurchaseReturn).WithMany(r => r.Details)
         .HasForeignKey(x => x.PurchaseReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseDetail).WithMany().HasForeignKey(x => x.PurchaseDetailId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.PurchaseReturnId, x.LineNumber })
         .HasDatabaseName("IX_PurchaseReturnDetails_ReturnId");
    }
}
