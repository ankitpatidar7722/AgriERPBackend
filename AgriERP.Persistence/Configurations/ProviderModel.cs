using AgriERP.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

/// <summary>
/// Which relational provider the model is being built for, made available to the
/// stateless IEntityTypeConfiguration classes and the ColumnShapes helpers.
///
/// EF instantiates those configurations with no constructor arguments (through
/// ApplyConfigurationsFromAssembly), so the provider cannot be injected. Model
/// building is synchronous and single-threaded, so a [ThreadStatic] flag set at
/// the top of OnModelCreating is correctly scoped - even when two contexts on
/// two providers build their models on different threads in one process, which
/// is exactly what the dual-provider tests do.
/// </summary>
internal static class ModelBuildProvider
{
    [ThreadStatic]
    private static bool _isNpgsql;

    /// <summary>True while a PostgreSQL model is being built on this thread.</summary>
    public static bool IsNpgsql => _isNpgsql;

    public static void Begin(string? providerName)
        => _isNpgsql = providerName is not null
                    && providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    public static void End() => _isNpgsql = false;
}

/// <summary>
/// Optimistic-concurrency mapping that differs by provider. SQL Server has a
/// native ROWVERSION; PostgreSQL does not, and uses its system xmin column
/// instead. Both give the same result - a DbUpdateConcurrencyException when two
/// users save the same row - without any call site needing to know which.
/// </summary>
internal static class ConcurrencyShapes
{
    public static EntityTypeBuilder<T> HasRowVersionConcurrency<T>(this EntityTypeBuilder<T> b)
        where T : class, IHasRowVersion
    {
        if (ModelBuildProvider.IsNpgsql)
        {
            // No ROWVERSION column on PostgreSQL. Nothing populates the byte[]
            // there, so drop it from the model and let the always-present xmin
            // system column carry the concurrency token - no schema column, and
            // no frontend change (RowVersion is never read on the client). The
            // shadow uint "xmin" property is the supported replacement for the
            // now-deprecated UseXminAsConcurrencyToken().
            b.Ignore(nameof(IHasRowVersion.RowVersion));
            b.Property<uint>("xmin").IsRowVersion();
        }
        else
        {
            b.Property(x => x.RowVersion).IsRowVersion();
        }

        return b;
    }
}
