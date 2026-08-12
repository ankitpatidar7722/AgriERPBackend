using AgriERP.Domain.Entities.Masters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

public class StateConfiguration : IEntityTypeConfiguration<State>
{
    public void Configure(EntityTypeBuilder<State> b)
    {
        b.ToTable("States");
        b.HasKey(x => x.StateId);

        // StateId IS the GST state code, so it is supplied, not generated.
        b.Property(x => x.StateId).ValueGeneratedNever();
        b.Property(x => x.StateCode).HasColumnType("char(2)").IsRequired();
        b.Property(x => x.StateName).HasMaxLength(60).IsRequired();
        b.Property(x => x.StateAbbr).HasMaxLength(5);

        b.HasIndex(x => x.StateCode).IsUnique().HasDatabaseName("UQ_States_StateCode");
        b.HasIndex(x => x.StateName).IsUnique().HasDatabaseName("UQ_States_StateName");
    }
}

public class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.ToTable("Units");
        b.HasKey(x => x.UnitId);

        b.Property(x => x.UnitCode).HasMaxLength(10).IsRequired();
        b.Property(x => x.UnitName).HasMaxLength(50).IsRequired();
        b.Property(x => x.Description).HasMaxLength(150);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasIndex(x => x.UnitCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Units_UnitCode");
    }
}

public class GstSlabConfiguration : IEntityTypeConfiguration<GstSlab>
{
    public void Configure(EntityTypeBuilder<GstSlab> b)
    {
        b.ToTable("GstSlabs");
        b.HasKey(x => x.GstSlabId);

        b.Property(x => x.SlabName).HasMaxLength(30).IsRequired();
        b.Property(x => x.TotalRate).AsPercent();
        b.Property(x => x.CgstRate).AsPercent();
        b.Property(x => x.SgstRate).AsPercent();
        b.Property(x => x.IgstRate).AsPercent();
        b.Property(x => x.CessRate).AsPercent();
        b.Property(x => x.EffectiveFrom).AsDate();

        b.HasIndex(x => x.TotalRate).IsUnique().HasDatabaseName("UQ_GstSlabs_TotalRate");
    }
}

public class HsnCodeConfiguration : IEntityTypeConfiguration<HsnCode>
{
    public void Configure(EntityTypeBuilder<HsnCode> b)
    {
        b.ToTable("HsnCodes");
        b.HasKey(x => x.HsnId);

        // Property is Code; the column is HsnCode, which would otherwise clash
        // with the entity name in queries.
        b.Property(x => x.Code).HasColumnName("HsnCode").HasMaxLength(10).IsRequired();
        b.Property(x => x.Description).HasMaxLength(250).IsRequired();

        b.HasOne(x => x.DefaultGstSlab)
         .WithMany()
         .HasForeignKey(x => x.DefaultGstSlabId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_HsnCodes_HsnCode");
    }
}

public class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> b)
    {
        b.ToTable("StorageLocations");
        b.HasKey(x => x.LocationId);

        b.Property(x => x.LocationCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.LocationName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LocationType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(300);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.ParentLocation)
         .WithMany(x => x.ChildLocations)
         .HasForeignKey(x => x.ParentLocationId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.LocationCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_StorageLocations_Code");
    }
}

public class VoucherMasterConfiguration : IEntityTypeConfiguration<VoucherMaster>
{
    public void Configure(EntityTypeBuilder<VoucherMaster> b)
    {
        b.ToTable("VoucherMaster");
        b.HasKey(x => x.VoucherId);

        b.Property(x => x.VoucherCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.VoucherName).HasMaxLength(60).IsRequired();
        b.Property(x => x.VoucherType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Prefix).HasMaxLength(15).IsRequired();
        b.Property(x => x.CreatedAt).AsCreatedAt();

        b.HasIndex(x => x.VoucherCode).IsUnique().HasDatabaseName("UQ_VoucherMaster_Code");
    }
}

public class ItemSubGroupConfiguration : IEntityTypeConfiguration<ItemSubGroup>
{
    public void Configure(EntityTypeBuilder<ItemSubGroup> b)
    {
        b.ToTable("ItemSubGroupMaster");
        b.HasKey(x => x.ItemSubGroupId);

        b.Property(x => x.ItemSubGroupCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.ItemSubGroupName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.IconName).HasMaxLength(40);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.ItemGroup).WithMany(g => g.SubGroups).HasForeignKey(x => x.ItemGroupId)
         .OnDelete(DeleteBehavior.Restrict);

        // Restrict: a sub-group with children must not be deletable.
        b.HasOne(x => x.ParentItemSubGroup)
         .WithMany(x => x.ChildItemSubGroups)
         .HasForeignKey(x => x.ParentItemSubGroupId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ItemSubGroupCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_ItemSubGroups_Code");
        b.HasIndex(x => x.ItemSubGroupName).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_ItemSubGroups_Name");
    }
}

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("Companies");
        b.HasKey(x => x.CompanyId);

        b.Property(x => x.CompanyCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.CompanyName).HasMaxLength(150).IsRequired();
        b.Property(x => x.GstNumber).HasColumnType("varchar(15)");
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Pincode).HasColumnType("varchar(6)");
        b.Property(x => x.Phone).HasMaxLength(15);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.Website).HasMaxLength(200);
        b.Property(x => x.ContactPerson).HasMaxLength(120);
        b.Property(x => x.LogoPath).HasMaxLength(300);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.State).WithMany().HasForeignKey(x => x.StateId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.CompanyCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Companies_Code");
        b.HasIndex(x => x.CompanyName).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Companies_Name");
    }
}

public class ShopMasterConfiguration : IEntityTypeConfiguration<ShopMaster>
{
    public void Configure(EntityTypeBuilder<ShopMaster> b)
    {
        b.ToTable("ShopMaster");
        b.HasKey(x => x.ShopId);

        b.Property(x => x.ShopName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.GstNo).HasMaxLength(15);
        b.Property(x => x.OwnerName).HasMaxLength(120);
        b.Property(x => x.MobileNo).HasMaxLength(15);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.State).WithMany().HasForeignKey(x => x.StateId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ShopName).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_ShopMaster_Name");
    }
}

public class WarehouseMasterConfiguration : IEntityTypeConfiguration<WarehouseMaster>
{
    public void Configure(EntityTypeBuilder<WarehouseMaster> b)
    {
        b.ToTable("WarehouseMaster");
        b.HasKey(x => x.WarehouseId);

        b.Property(x => x.WarehouseCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.WarehouseName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasMany(x => x.Bins).WithOne(x => x.Warehouse)
         .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.WarehouseCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_WarehouseMaster_Code");
    }
}

public class WarehouseBinConfiguration : IEntityTypeConfiguration<WarehouseBin>
{
    public void Configure(EntityTypeBuilder<WarehouseBin> b)
    {
        b.ToTable("WarehouseBins");
        b.HasKey(x => x.WarehouseBinId);

        b.Property(x => x.BinName).HasMaxLength(100).IsRequired();

        b.HasIndex(x => x.WarehouseId).HasDatabaseName("IX_WarehouseBins_WarehouseId");
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("Suppliers");
        b.HasKey(x => x.SupplierId);

        b.Property(x => x.SupplierCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.SupplierName).HasMaxLength(150).IsRequired();
        b.Property(x => x.GstNumber).HasColumnType("varchar(15)");
        b.Property(x => x.PanNumber).HasColumnType("varchar(10)");
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Pincode).HasColumnType("varchar(6)");
        b.Property(x => x.Phone).HasMaxLength(15);
        b.Property(x => x.AlternatePhone).HasMaxLength(15);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.ContactPerson).HasMaxLength(120);

        b.Property(x => x.CreditLimit).AsAmount();
        b.Property(x => x.OpeningBalance).AsAmount();
        b.Property(x => x.OpeningBalanceType).HasConversion<string>().HasColumnType("char(2)").IsRequired();
        b.Property(x => x.OpeningBalanceDate).AsNullableDate();

        b.Property(x => x.BankName).HasMaxLength(120);
        b.Property(x => x.BankAccountNumber).HasMaxLength(30);
        b.Property(x => x.BankIfsc).HasColumnType("varchar(11)");
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.State).WithMany().HasForeignKey(x => x.StateId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.SupplierCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Suppliers_Code");
        b.HasIndex(x => x.GstNumber).IsUnique()
         .HasFilter("[IsDeleted] = 0 AND [GstNumber] IS NOT NULL")
         .HasDatabaseName("UQ_Suppliers_GstNumber");
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.HasKey(x => x.CustomerId);

        b.Property(x => x.CustomerCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(150).IsRequired();
        b.Property(x => x.FatherName).HasMaxLength(150);
        b.Property(x => x.Village).HasMaxLength(100);
        b.Property(x => x.Mobile).HasMaxLength(15);
        b.Property(x => x.AlternateMobile).HasMaxLength(15);
        b.Property(x => x.GstNumber).HasColumnType("varchar(15)");
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Pincode).HasColumnType("varchar(6)");

        b.Property(x => x.CustomerType).HasConversion<string>().HasMaxLength(15).IsRequired();
        b.Property(x => x.CreditLimit).AsAmount();
        b.Property(x => x.OpeningBalance).AsAmount();
        b.Property(x => x.OpeningBalanceType).HasConversion<string>().HasColumnType("char(2)").IsRequired();
        b.Property(x => x.OpeningBalanceDate).AsNullableDate();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.State).WithMany().HasForeignKey(x => x.StateId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.CustomerCode).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Customers_Code");
        // Mobile is how the counter looks a farmer up, but optional: pure cash
        // walk-ins are billed without being created as a customer at all.
        b.HasIndex(x => x.Mobile).IsUnique()
         .HasFilter("[IsDeleted] = 0 AND [Mobile] IS NOT NULL")
         .HasDatabaseName("UQ_Customers_Mobile");
    }
}
