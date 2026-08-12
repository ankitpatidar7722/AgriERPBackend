using AgriERP.Domain.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

public class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> b)
    {
        b.ToTable("CompanyProfile");
        b.HasKey(x => x.CompanyProfileId);

        // Not an identity: CK_CompanyProfile_Singleton pins it to 1, which is
        // how "exactly one shop" is enforced.
        b.Property(x => x.CompanyProfileId).ValueGeneratedNever();

        b.Property(x => x.ShopName).HasMaxLength(150).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(150);
        b.Property(x => x.GstNumber).HasColumnType("varchar(15)");
        b.Property(x => x.PanNumber).HasColumnType("varchar(10)");
        b.Property(x => x.PesticideLicenceNo).HasMaxLength(50);
        b.Property(x => x.SeedLicenceNo).HasMaxLength(50);
        b.Property(x => x.FertilizerLicenceNo).HasMaxLength(50);

        b.Property(x => x.AddressLine1).HasMaxLength(200);
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Pincode).HasColumnType("varchar(6)");
        b.Property(x => x.Phone).HasMaxLength(15);
        b.Property(x => x.AlternatePhone).HasMaxLength(15);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.Website).HasMaxLength(200);

        b.Property(x => x.BankName).HasMaxLength(120);
        b.Property(x => x.BankAccountNumber).HasMaxLength(30);
        b.Property(x => x.BankIfsc).HasColumnType("varchar(11)");
        b.Property(x => x.BankBranch).HasMaxLength(120);
        b.Property(x => x.UpiId).HasMaxLength(100);

        b.Property(x => x.LogoPath).HasMaxLength(300);
        b.Property(x => x.InvoiceTerms).HasMaxLength(1000);
        b.Property(x => x.InvoiceFooterNote).HasMaxLength(500);
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.State).WithMany().HasForeignKey(x => x.StateId)
         .OnDelete(DeleteBehavior.Restrict);
    }
}

public class FinancialYearConfiguration : IEntityTypeConfiguration<FinancialYear>
{
    public void Configure(EntityTypeBuilder<FinancialYear> b)
    {
        b.ToTable("FinancialYears");
        b.HasKey(x => x.FinancialYearId);

        b.Property(x => x.YearCode).HasMaxLength(10).IsRequired();
        b.Property(x => x.StartDate).AsDate();
        b.Property(x => x.EndDate).AsDate();
        b.Property(x => x.ClosedAt).AsNullableTimestamp();
        b.Property(x => x.CreatedAt).AsCreatedAt();

        b.HasIndex(x => x.YearCode).IsUnique().HasDatabaseName("UQ_FinancialYears_YearCode");
        // Exactly one active year.
        b.HasIndex(x => x.IsActive).IsUnique().HasFilter("[IsActive] = 1")
         .HasDatabaseName("UQ_FinancialYears_Active");
    }
}

public class NumberSeriesConfiguration : IEntityTypeConfiguration<NumberSeries>
{
    public void Configure(EntityTypeBuilder<NumberSeries> b)
    {
        b.ToTable("NumberSeries");
        b.HasKey(x => x.NumberSeriesId);

        b.Property(x => x.DocumentType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
        b.Property(x => x.Suffix).HasMaxLength(20).IsRequired();
        b.Property(x => x.Separator).HasMaxLength(3).IsRequired();
        b.Property(x => x.PaddingLength).HasColumnType("tinyint");
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();

        b.HasOne(x => x.FinancialYear).WithMany(f => f.NumberSeries)
         .HasForeignKey(x => x.FinancialYearId).OnDelete(DeleteBehavior.Restrict);

        // One live counter per document type per year; two would produce duplicates.
        b.HasIndex(x => new { x.DocumentType, x.FinancialYearId }).IsUnique()
         .HasDatabaseName("UQ_NumberSeries_Type_Year");
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("AppSettings");
        b.HasKey(x => x.SettingId);

        b.Property(x => x.SettingKey).HasMaxLength(80).IsRequired();
        b.Property(x => x.SettingValue).HasMaxLength(500);
        // Stored lower-case in SQL ('string','int','bool'), so the conversion
        // spells the mapping out rather than relying on enum casing.
        b.Property(x => x.DataType)
         .HasConversion(
             v => v.ToString().ToLowerInvariant(),
             // Positional, not named: an expression tree cannot carry named arguments.
             v => Enum.Parse<Domain.Enums.SettingDataType>(v, true))
         .HasMaxLength(20).IsRequired();
        b.Property(x => x.Category).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();

        b.HasIndex(x => x.SettingKey).IsUnique().HasDatabaseName("UQ_AppSettings_SettingKey");
    }
}

public class ModuleMasterConfiguration : IEntityTypeConfiguration<ModuleMaster>
{
    public void Configure(EntityTypeBuilder<ModuleMaster> b)
    {
        b.ToTable("ModuleMaster");
        b.HasKey(x => x.ModuleId);

        // The column is ModuleID; the property follows C# casing.
        b.Property(x => x.ModuleId).HasColumnName("ModuleID");

        b.Property(x => x.ModuleName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ModuleDisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ModuleHeadName).HasMaxLength(100).IsRequired();
        b.Property(x => x.ModuleHeadDisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.IconName).HasMaxLength(100);

        // DATETIME, not the datetime2(3) used elsewhere: this column records
        // when a menu entry was added, which nothing reads to the millisecond.
        b.Property(x => x.CreatedDate)
         .HasColumnType("datetime")
         .HasDefaultValueSql("GETDATE()");

        b.HasIndex(x => x.ModuleName).IsUnique()
         .HasFilter("[IsDeletedTransaction] = 0")
         .HasDatabaseName("UQ_ModuleMaster_ModuleName");

        b.HasIndex(x => new { x.IsDeletedTransaction, x.ModuleHeadDisplayOrder, x.ModuleDisplayOrder })
         .HasDatabaseName("IX_ModuleMaster_Display");
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.AuditLogId);

        b.Property(x => x.TableName).HasMaxLength(120).IsRequired();
        b.Property(x => x.PrimaryKeyValue).HasMaxLength(60);
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(x => x.OldValues).HasColumnType("nvarchar(max)");
        b.Property(x => x.NewValues).HasColumnType("nvarchar(max)");
        b.Property(x => x.ChangedColumns).HasMaxLength(1000);
        b.Property(x => x.UserName).HasMaxLength(60);
        b.Property(x => x.IpAddress).HasMaxLength(45);
        b.Property(x => x.RequestId).HasMaxLength(50);
        b.Property(x => x.LoggedAt).AsCreatedAt();

        b.HasIndex(x => new { x.TableName, x.PrimaryKeyValue, x.LoggedAt })
         .HasDatabaseName("IX_AuditLogs_Table_Key");
    }
}
