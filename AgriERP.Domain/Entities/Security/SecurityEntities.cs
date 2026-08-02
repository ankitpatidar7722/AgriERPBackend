using AgriERP.Domain.Common;

namespace AgriERP.Domain.Entities.Security;

/// <summary>Maps to Roles. No soft delete: a role in use must not disappear.</summary>
public class Role : AuditableEntity, IHasRowVersion
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;

    /// <summary>Computed by SQL Server as UPPER(RoleName); used for case-insensitive lookup.</summary>
    public string? NormalizedName { get; private set; }

    public string? Description { get; set; }

    /// <summary>System roles cannot be renamed or deleted from the UI.</summary>
    public bool IsSystemRole { get; set; }

    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

/// <summary>
/// Maps to Permissions. Permissions are codes ("Product.Create"), not
/// booleans per screen, so a new module means inserting rows, never altering
/// a table.
/// </summary>
public class Permission
{
    public int PermissionId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

/// <summary>Maps to RolePermissions. Composite key (RoleId, PermissionId).</summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public DateTime GrantedAt { get; set; }
    public int? GrantedBy { get; set; }

    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}

/// <summary>Maps to Users.</summary>
public class User : MasterEntity
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? NormalizedUserName { get; private set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; private set; }

    /// <summary>BCrypt hash written by the API. Never a plain or reversible value.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Rotated on password or role change. The API stamps it into the JWT, so
    /// changing it invalidates every token already issued to this user.
    /// </summary>
    public Guid SecurityStamp { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public int RoleId { get; set; }
    public string? AvatarPath { get; set; }

    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastPasswordChangeAt { get; set; }

    /// <summary>Brute-force throttling. Reset to zero on a successful login.</summary>
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }

    public Role? Role { get; set; }
    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = new List<UserRefreshToken>();
}

/// <summary>
/// Maps to UserRefreshTokens. The token itself is never stored - only its
/// SHA-256 hash, so a leaked backup cannot be replayed as a live session.
/// </summary>
public class UserRefreshToken
{
    public long RefreshTokenId { get; set; }
    public int UserId { get; set; }
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }

    /// <summary>
    /// Set when this token was rotated. A reuse attempt on an already-rotated
    /// token indicates theft, and should revoke the whole chain.
    /// </summary>
    public long? ReplacedByTokenId { get; set; }

    public User? User { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}

/// <summary>Maps to UserPasswordResets.</summary>
public class UserPasswordReset
{
    public long PasswordResetId { get; set; }
    public int UserId { get; set; }
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? RequestedByIp { get; set; }

    public User? User { get; set; }
}
