using AgriERP.Domain.Entities.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

public class ItemGroupConfiguration : IEntityTypeConfiguration<ItemGroup>
{
    public void Configure(EntityTypeBuilder<ItemGroup> b)
    {
        b.ToTable("ItemGroupMaster");
        b.HasKey(x => x.ItemGroupId);

        b.Property(x => x.ItemGroupCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.ItemGroupName).HasMaxLength(100).IsRequired();
        b.Property(x => x.ItemCodePrefix).HasMaxLength(10).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.IconName).HasMaxLength(50);

        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class ItemGroupFieldConfiguration : IEntityTypeConfiguration<ItemGroupField>
{
    public void Configure(EntityTypeBuilder<ItemGroupField> b)
    {
        b.ToTable("ItemGroupFieldMaster");
        b.HasKey(x => x.ItemGroupFieldId);

        b.Property(x => x.FieldName).HasMaxLength(128).IsRequired();
        b.Property(x => x.FieldDisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.HelpText).HasMaxLength(300);
        b.Property(x => x.FieldType).HasMaxLength(20).IsRequired();
        b.Property(x => x.SectionName).HasMaxLength(60);
        b.Property(x => x.DefaultValue).HasMaxLength(200);
        b.Property(x => x.LookupSource).HasMaxLength(60);
        b.Property(x => x.UnitLabel).HasMaxLength(30);

        // Bounds are compared against decimal fields, so they carry the same
        // precision - a rate ceiling of 999.99995 must not round on the way in.
        b.Property(x => x.MinValue).HasPrecision(18, 4);
        b.Property(x => x.MaxValue).HasPrecision(18, 4);

        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.ItemGroup).WithMany(g => g.Fields).HasForeignKey(x => x.ItemGroupId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class ItemMasterDetailConfiguration : IEntityTypeConfiguration<ItemMasterDetail>
{
    public void Configure(EntityTypeBuilder<ItemMasterDetail> b)
    {
        b.ToTable("ItemMasterDetails");
        b.HasKey(x => x.ItemDetailId);

        b.Property(x => x.FieldValue).HasMaxLength(500);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();

        b.HasOne(x => x.Item).WithMany(i => i.ExtraFieldValues).HasForeignKey(x => x.ItemId)
         .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Field).WithMany().HasForeignKey(x => x.ItemGroupFieldId)
         .OnDelete(DeleteBehavior.Restrict);

        // Mirrors UQ_ItemMasterDetails_Item_Field. Without it a save that runs
        // twice leaves two answers to the same question.
        b.HasIndex(x => new { x.ItemId, x.ItemGroupFieldId }).IsUnique();
    }
}
