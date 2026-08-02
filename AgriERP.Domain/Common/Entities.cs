namespace AgriERP.Domain.Common;

/// <summary>Tracks who created or last changed a row.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    int? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    int? UpdatedBy { get; set; }
}

/// <summary>
/// Rows that are hidden rather than removed. Masters are never hard-deleted:
/// a supplier referenced by three-year-old purchase bills must stay resolvable
/// or those bills lose their party name.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}

/// <summary>
/// Maps to a SQL Server ROWVERSION for optimistic concurrency. Two users
/// editing the same item produce a clean DbUpdateConcurrencyException
/// instead of one silently overwriting the other.
/// </summary>
public interface IHasRowVersion
{
    byte[]? RowVersion { get; set; }
}

public abstract class AuditableEntity : IAuditable
{
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

/// <summary>Base for master tables: auditable, soft-deletable, versioned.</summary>
public abstract class MasterEntity : AuditableEntity, ISoftDeletable, IHasRowVersion
{
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Base for transaction headers (purchase, sale, adjustment, transfer).
/// Deliberately NOT soft-deletable: a posted document is cancelled by status
/// and reversed in the stock journal, never deleted.
/// </summary>
public abstract class DocumentEntity : AuditableEntity, IHasRowVersion
{
    public byte[]? RowVersion { get; set; }
    public int? FinancialYearId { get; set; }
}
