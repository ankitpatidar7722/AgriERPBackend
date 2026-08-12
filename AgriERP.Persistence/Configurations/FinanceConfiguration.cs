using AgriERP.Domain.Entities.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

public class PaymentModeConfiguration : IEntityTypeConfiguration<PaymentMode>
{
    public void Configure(EntityTypeBuilder<PaymentMode> b)
    {
        b.ToTable("PaymentModes");
        b.HasKey(x => x.PaymentModeId);

        b.Property(x => x.ModeCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.ModeName).HasMaxLength(50).IsRequired();

        b.HasIndex(x => x.ModeCode).IsUnique().HasDatabaseName("UQ_PaymentModes_Code");
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments");
        b.HasKey(x => x.PaymentId);

        b.Property(x => x.VoucherNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.PaymentDate).AsDate();
        b.Property(x => x.PartyType).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(x => x.PaymentType).HasConversion<string>().HasMaxLength(10).IsRequired();

        b.Property(x => x.Amount).AsAmount();
        b.Property(x => x.AllocatedAmount).AsAmount();
        // Whatever is left un-applied is a genuine on-account advance, not
        // something to be silently spread across open bills.
        b.Property(x => x.UnallocatedAmount).AsAmount()
         .AsComputed("[Amount]-[AllocatedAmount]");

        b.Property(x => x.ReferenceNumber).HasMaxLength(60);
        b.Property(x => x.BankName).HasMaxLength(120);
        b.Property(x => x.ChequeDate).AsNullableDate();
        b.Property(x => x.ClearanceStatus).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.ClearedDate).AsNullableDate();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.CancelledAt).AsNullableTimestamp();
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId)
         .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PaymentMode).WithMany().HasForeignKey(x => x.PaymentModeId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.VoucherNumber).IsUnique().HasDatabaseName("UQ_Payments_VoucherNumber");
        b.HasIndex(x => x.PaymentDate).HasDatabaseName("IX_Payments_Date");
    }
}

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> b)
    {
        b.ToTable("PaymentAllocations");
        b.HasKey(x => x.PaymentAllocationId);

        b.Property(x => x.ReferenceType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ReferenceNumber).HasMaxLength(30);
        b.Property(x => x.AllocatedAmount).AsAmount();
        b.Property(x => x.AllocatedAt).AsCreatedAt();

        b.HasOne(x => x.Payment).WithMany(p => p.Allocations).HasForeignKey(x => x.PaymentId)
         .OnDelete(DeleteBehavior.Cascade);

        // One payment settles a given bill once; a second instalment is a
        // second payment, not a second allocation row on the same one.
        b.HasIndex(x => new { x.PaymentId, x.ReferenceType, x.ReferenceId }).IsUnique()
         .HasDatabaseName("UQ_PaymentAllocations_Payment_Reference");
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
         .HasDatabaseName("IX_PaymentAllocations_Reference");
    }
}

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable("ExpenseCategories");
        b.HasKey(x => x.ExpenseCategoryId);

        b.Property(x => x.CategoryCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.CategoryName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasIndex(x => x.CategoryCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_ExpenseCategories_Code");
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("Expenses");
        b.HasKey(x => x.ExpenseId);

        b.Property(x => x.VoucherNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.ExpenseDate).AsDate();
        b.Property(x => x.PaidTo).HasMaxLength(150);
        b.Property(x => x.Amount).AsAmount();
        b.Property(x => x.GstAmount).AsAmount();
        b.Property(x => x.TotalAmount).AsAmount().AsComputed("[Amount]+[GstAmount]");
        b.Property(x => x.ReferenceNumber).HasMaxLength(60);
        b.Property(x => x.BillNumber).HasMaxLength(50);
        b.Property(x => x.AttachmentPath).HasMaxLength(300);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.ExpenseCategory).WithMany(c => c.Expenses)
         .HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PaymentMode).WithMany().HasForeignKey(x => x.PaymentModeId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.VoucherNumber).IsUnique().HasDatabaseName("UQ_Expenses_VoucherNumber");
        b.HasIndex(x => x.ExpenseDate).HasDatabaseName("IX_Expenses_Date");
    }
}
