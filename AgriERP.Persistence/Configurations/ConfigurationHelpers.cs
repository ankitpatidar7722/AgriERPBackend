// HasColumnType / HasComputedColumnSql / HasDefaultValueSql are relational
// extensions, declared in the Microsoft.EntityFrameworkCore namespace itself.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

/// <summary>
/// Shared column shapes. Precision is not decoration - a rate stored at two
/// decimals silently rounds 380.4550 to 380.46 on every consignment, and the
/// error compounds through valuation and profit. These four shapes match the
/// DECIMAL definitions in database/scripts/ exactly.
/// </summary>
internal static class ColumnShapes
{
    /// <summary>DECIMAL(18,3) - quantities. Seeds sell in fractions of a kilo.</summary>
    public static PropertyBuilder<decimal> AsQuantity(this PropertyBuilder<decimal> b)
        => b.HasPrecision(18, 3);

    /// <summary>DECIMAL(18,4) - unit rates. Agri rates carry paise-level precision.</summary>
    public static PropertyBuilder<decimal> AsRate(this PropertyBuilder<decimal> b)
        => b.HasPrecision(18, 4);

    /// <summary>DECIMAL(18,2) - money totals.</summary>
    public static PropertyBuilder<decimal> AsAmount(this PropertyBuilder<decimal> b)
        => b.HasPrecision(18, 2);

    /// <summary>DECIMAL(6,3) - percentages.</summary>
    public static PropertyBuilder<decimal> AsPercent(this PropertyBuilder<decimal> b)
        => b.HasPrecision(6, 3);

    public static PropertyBuilder<decimal?> AsNullableQuantity(this PropertyBuilder<decimal?> b)
        => b.HasPrecision(18, 3);

    public static PropertyBuilder<decimal?> AsNullableRate(this PropertyBuilder<decimal?> b)
        => b.HasPrecision(18, 4);

    /// <summary>
    /// SQL DATE, not DATETIME2. Business dates are calendar days: an invoice
    /// raised at 11pm belongs to that day, and a time component only invites
    /// off-by-one-day bugs in date-range reports.
    /// </summary>
    public static PropertyBuilder<DateTime> AsDate(this PropertyBuilder<DateTime> b)
        => b.HasColumnType("date");

    public static PropertyBuilder<DateTime?> AsNullableDate(this PropertyBuilder<DateTime?> b)
        => b.HasColumnType("date");

    /// <summary>Millisecond-precision audit timestamp, stored UTC. datetime2(3)
    /// on SQL Server, timestamp(3) on PostgreSQL.</summary>
    public static PropertyBuilder<DateTime> AsTimestamp(this PropertyBuilder<DateTime> b)
        => b.HasColumnType(TimestampType);

    public static PropertyBuilder<DateTime?> AsNullableTimestamp(this PropertyBuilder<DateTime?> b)
        => b.HasColumnType(TimestampType);

    /// <summary>
    /// CreatedAt: DB default SYSUTCDATETIME() applies when the property is left
    /// at default(DateTime), and an explicitly set value still wins.
    /// </summary>
    public static PropertyBuilder<DateTime> AsCreatedAt(this PropertyBuilder<DateTime> b)
        => b.HasColumnType(TimestampType).HasDefaultValueSql(UtcNowSql);

    /// <summary>
    /// A PERSISTED computed column. EF must never write these - the database
    /// owns the arithmetic, which is what stops a stored total from drifting
    /// away from the lines that produced it.
    /// </summary>
    public static PropertyBuilder<T> AsComputed<T>(this PropertyBuilder<T> b, string sql)
        => b.HasComputedColumnSql(sql, stored: true).ValueGeneratedOnAddOrUpdate();

    // datetime2 is SQL Server's; PostgreSQL's equivalent is timestamp. Both
    // hold a wall-clock UTC value at millisecond precision here.
    private static string TimestampType
        => ModelBuildProvider.IsNpgsql ? "timestamp(3)" : "datetime2(3)";

    // A UTC "now" default in each dialect. PostgreSQL's now() is timestamptz,
    // converted to a plain timestamp at UTC to match the timestamp(3) column.
    private static string UtcNowSql
        => ModelBuildProvider.IsNpgsql ? "(now() at time zone 'utc')" : "SYSUTCDATETIME()";
}
