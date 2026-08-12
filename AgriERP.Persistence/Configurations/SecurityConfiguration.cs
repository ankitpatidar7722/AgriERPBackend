using AgriERP.Domain.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.RoleId);

        b.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(50).AsComputed("UPPER([RoleName])");
        b.Property(x => x.Description).HasMaxLength(250);
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasIndex(x => x.RoleName).IsUnique().HasDatabaseName("UQ_Roles_RoleName");
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(x => x.PermissionId);

        b.Property(x => x.PermissionCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.Module).HasMaxLength(50).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(250);

        b.HasIndex(x => x.PermissionCode).IsUnique().HasDatabaseName("UQ_Permissions_Code");
        b.HasIndex(x => new { x.Module, x.DisplayOrder }).HasDatabaseName("IX_Permissions_Module");
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(x => new { x.RoleId, x.PermissionId });

        b.Property(x => x.GrantedAt).AsCreatedAt();

        b.HasOne(x => x.Role)
         .WithMany(r => r.RolePermissions)
         .HasForeignKey(x => x.RoleId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Permission)
         .WithMany(p => p.RolePermissions)
         .HasForeignKey(x => x.PermissionId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.UserId);

        b.Property(x => x.UserName).HasMaxLength(60).IsRequired();
        b.Property(x => x.NormalizedUserName).HasMaxLength(60).AsComputed("UPPER([UserName])");
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.NormalizedEmail).HasMaxLength(150).AsComputed("UPPER([Email])");
        b.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
        b.Property(x => x.SecurityStamp).HasDefaultValueSql("NEWID()");
        b.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Mobile).HasMaxLength(15);
        b.Property(x => x.AvatarPath).HasMaxLength(300);

        b.Property(x => x.LastLoginAt).AsNullableTimestamp();
        b.Property(x => x.LastPasswordChangeAt).AsNullableTimestamp();
        b.Property(x => x.LockoutEndAt).AsNullableTimestamp();
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.UpdatedAt).AsNullableTimestamp();
        b.HasRowVersionConcurrency();

        b.HasOne(x => x.Role)
         .WithMany(r => r.Users)
         .HasForeignKey(x => x.RoleId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.NormalizedUserName).IsUnique().HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UQ_Users_UserName");
        b.HasIndex(x => x.NormalizedEmail).IsUnique()
         .HasFilter("[IsDeleted] = 0 AND [Email] IS NOT NULL")
         .HasDatabaseName("UQ_Users_Email");
    }
}

public class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> b)
    {
        b.ToTable("UserRefreshTokens");
        b.HasKey(x => x.RefreshTokenId);

        // VARBINARY(32): the SHA-256 of the opaque token given to the client.
        // The token itself is never persisted.
        b.Property(x => x.TokenHash).HasColumnType("varbinary(32)").IsRequired();
        b.Property(x => x.ExpiresAt).AsTimestamp();
        b.Property(x => x.CreatedAt).AsCreatedAt();
        b.Property(x => x.CreatedByIp).HasMaxLength(45);      // 45 covers IPv6
        b.Property(x => x.UserAgent).HasMaxLength(300);
        b.Property(x => x.RevokedAt).AsNullableTimestamp();
        b.Property(x => x.RevokedByIp).HasMaxLength(45);

        b.Ignore(x => x.IsActive);   // computed in C#, not a column

        b.HasOne(x => x.User)
         .WithMany(u => u.RefreshTokens)
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UQ_UserRefreshTokens_TokenHash");
    }
}

public class UserPasswordResetConfiguration : IEntityTypeConfiguration<UserPasswordReset>
{
    public void Configure(EntityTypeBuilder<UserPasswordReset> b)
    {
        b.ToTable("UserPasswordResets");
        b.HasKey(x => x.PasswordResetId);

        b.Property(x => x.TokenHash).HasColumnType("varbinary(32)").IsRequired();
        b.Property(x => x.ExpiresAt).AsTimestamp();
        b.Property(x => x.UsedAt).AsNullableTimestamp();
        b.Property(x => x.RequestedAt).AsCreatedAt();
        b.Property(x => x.RequestedByIp).HasMaxLength(45);

        b.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UQ_UserPasswordResets_TokenHash");
    }
}
