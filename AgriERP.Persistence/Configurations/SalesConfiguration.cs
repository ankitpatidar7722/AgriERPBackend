using AgriERP.Domain.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

// Repeated verbatim from 07_Sales.sql - see the note in PurchaseComputedSql.
internal static class SalesComputedSql
{
    public const string GrandTotal =
        "[TaxableAmount]+[CgstAmount]+[SgstAmount]+[IgstAmount]+[CessAmount]" +
        "+[OtherCharges]+[RoundOff]";

    public const string BalanceAmount = GrandTotal + "-[ReceivedAmount]";

    public const string PaymentStatus =
        "case when [ReceivedAmount]<=(0) then N'Unpaid' " +
        "when [ReceivedAmount]>=(" + GrandTotal + ") then N'Paid' " +
        "else N'Partial' end";
}

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("Sales");
        b.HasKey(x => x.SaleId);

        b.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.InvoiceDate).AsDate();
        b.Property(x => x.InvoiceTime).HasColumnType("time(0)");
        b.Property(x => x.WalkInCustomerName).HasMaxLength(150);
        b.Property(x => x.WalkInMobile).HasMaxLength(15);
        b.Property(x => x.DueDate).AsNullableDate();

        b.Property(x => x.SaleType).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.PaymentType).HasConversion<string>().HasMaxLength(10).IsRequired();

        b.Property(x => x.GrossAmount).AsAmount();
        b.Property(x => x.DiscountAmount).AsAmount();
        b.Property(x => x.TaxableAmount).AsAmount();
        b.Property(x => x.CgstAmount).AsAmount();
        b.Property(x => x.SgstAmount).AsAmount();
        b.Property(x => x.IgstAmount).AsAmount();
        b.Property(x => x.CessAmount).AsAmount();
        b.Property(x => x.OtherCharges).AsAmount();
        b.Property(x => x.RoundOff).AsAmount();
        b.Property(x => x.TotalCostAmount).AsAmount();
        b.Property(x => x.ReceivedAmount).AsAmount();

        b.Property(x => x.GrandTotal).AsAmount().AsComputed(SalesComputedSql.GrandTotal);
        b.Property(x => x.GrossProfit).AsAmount()
         .AsComputed("[TaxableAmount]-[TotalCostAmount]");
        b.Property(x => x.BalanceAmount).AsAmount().AsComputed(SalesComputedSql.BalanceAmount);
        b.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(7)
         .AsComputed(SalesComputedSql.PaymentStatus);

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.PostedAt).AsNullableTimestamp();
        b.Property(x => x.CancelledAt).AsNullableTimestamp();
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.Ignore(x => x.DisplayCustomerName);   // resolved in C#, not a column

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Salesman).WithMany().HasForeignKey(x => x.SalesmanId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PlaceOfSupplyState).WithMany().HasForeignKey(x => x.PlaceOfSupplyStateId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.InvoiceNumber).IsUnique().HasDatabaseName("UQ_Sales_InvoiceNumber");
        b.HasIndex(x => x.InvoiceDate).HasDatabaseName("IX_Sales_InvoiceDate");
    }
}

public class SalesDetailConfiguration : IEntityTypeConfiguration<SalesDetail>
{
    public void Configure(EntityTypeBuilder<SalesDetail> b)
    {
        b.ToTable("SalesDetails");
        b.HasKey(x => x.SalesDetailId);

        b.Property(x => x.BatchNumber).HasMaxLength(50);
        b.Property(x => x.ExpiryDate).AsNullableDate();

        b.Property(x => x.Quantity).AsQuantity();
        b.Property(x => x.FreeQuantity).AsQuantity();
        b.Property(x => x.TotalQuantity).AsQuantity().AsComputed("[Quantity]+[FreeQuantity]");

        b.Property(x => x.Mrp).AsRate();
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

        b.Property(x => x.CostRate).AsRate();
        // Cost is charged on Quantity + FreeQuantity: free goods carry cost but
        // earn no revenue, which is what makes a scheme's true margin visible.
        b.Property(x => x.CostAmount).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),([Quantity]+[FreeQuantity])*[CostRate])");
        b.Property(x => x.LineProfit).AsAmount().AsComputed(
            "CONVERT([decimal](18,2),[Quantity]*[Rate])-[DiscountAmount]" +
            "-CONVERT([decimal](18,2),([Quantity]+[FreeQuantity])*[CostRate])");

        b.Property(x => x.HsnCode).HasMaxLength(10);
        b.Property(x => x.Remarks).HasMaxLength(300);

        b.HasOne(x => x.Sale).WithMany(s => s.Details).HasForeignKey(x => x.SaleId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.SaleId, x.LineNumber }).HasDatabaseName("IX_SalesDetails_SaleId");
        b.HasIndex(x => x.ItemId).HasDatabaseName("IX_SalesDetails_ItemId");
    }
}

public class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> b)
    {
        b.ToTable("SalePayments");
        b.HasKey(x => x.SalePaymentId);

        b.Property(x => x.Amount).AsAmount();
        b.Property(x => x.ReferenceNumber).HasMaxLength(60);
        b.Property(x => x.BankName).HasMaxLength(120);
        b.Property(x => x.ChequeDate).AsNullableDate();
        b.Property(x => x.Remarks).HasMaxLength(300);
        b.Property(x => x.CreatedAt).AsCreatedAt();

        b.HasOne(x => x.Sale).WithMany(s => s.Payments).HasForeignKey(x => x.SaleId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PaymentMode).WithMany().HasForeignKey(x => x.PaymentModeId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.SaleId).HasDatabaseName("IX_SalePayments_SaleId");
    }
}

public class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> b)
    {
        b.ToTable("SalesReturns");
        b.HasKey(x => x.SalesReturnId);

        b.Property(x => x.ReturnNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.ReturnDate).AsDate();
        b.Property(x => x.CreditNoteNumber).HasMaxLength(50);
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
        b.Property(x => x.TotalCostAmount).AsAmount();
        b.Property(x => x.RefundedAmount).AsAmount();
        b.Property(x => x.RefundMode).HasConversion<string>().HasMaxLength(15).IsRequired();

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.PostedAt).AsNullableTimestamp();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ReturnNumber).IsUnique().HasDatabaseName("UQ_SalesReturns_Number");
    }
}

public class SalesReturnDetailConfiguration : IEntityTypeConfiguration<SalesReturnDetail>
{
    public void Configure(EntityTypeBuilder<SalesReturnDetail> b)
    {
        b.ToTable("SalesReturnDetails");
        b.HasKey(x => x.SalesReturnDetailId);

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
        b.Property(x => x.CostRate).AsRate();
        b.Property(x => x.CostAmount).AsAmount()
         .AsComputed("CONVERT([decimal](18,2),[Quantity]*[CostRate])");
        b.Property(x => x.ReturnReason).HasMaxLength(300);

        b.HasOne(x => x.SalesReturn).WithMany(r => r.Details).HasForeignKey(x => x.SalesReturnId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalesDetail).WithMany().HasForeignKey(x => x.SalesDetailId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.SalesReturnId, x.LineNumber })
         .HasDatabaseName("IX_SalesReturnDetails_ReturnId");
    }
}
