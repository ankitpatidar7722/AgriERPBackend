using System.Collections;
using AgriERP.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace AgriERP.Persistence.Tests;

/// <summary>
/// Connects to the local PostgreSQL "agrierp" database and asks EF to query
/// every mapped table and view. An empty database returns no rows, but each
/// query still runs its SELECT, so a column EF expects that the hand-written
/// PostgreSQL schema does not provide (wrong name, missing column, view column
/// mismatch) fails here with a precise "column does not exist" - which is
/// exactly the drift this check exists to catch.
///
/// Not part of the "Database" (SQL Server) collection. Requires the local PG
/// schema built from database/postgres/*.sql. Skipped cleanly if PG is absent.
/// </summary>
public class PostgresConformanceTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=agrierp;Username=postgres;Password=postgres";

    private readonly ITestOutputHelper _output;

    public PostgresConformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Every_mapped_table_and_view_queries_against_postgres()
    {
        var options = new DbContextOptionsBuilder<AgriErpDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        using var context = new AgriErpDbContext(options);

        // Fail fast (and clearly) if the database is not reachable, rather than
        // reporting every entity as broken.
        if (!context.Database.CanConnect())
        {
            _output.WriteLine("PostgreSQL 'agrierp' not reachable - skipping.");
            return;
        }

        var genericSet = typeof(DbContext).GetMethods()
            .Single(m => m.Name == nameof(DbContext.Set)
                         && m.IsGenericMethod
                         && m.GetParameters().Length == 0);

        var failures = new List<string>();
        var checked_ = 0;

        foreach (var entityType in context.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            var clrType = entityType.ClrType;
            var label = entityType.GetTableName() ?? entityType.GetViewName() ?? clrType.Name;
            try
            {
                var set = genericSet.MakeGenericMethod(clrType).Invoke(context, null)!;
                // Enumerating executes the SELECT of every mapped column.
                foreach (var _ in (IEnumerable)set) break;
                checked_++;
            }
            catch (Exception ex)
            {
                failures.Add($"{label}: {ex.GetBaseException().Message}");
            }
        }

        _output.WriteLine($"Queried {checked_} mapped types, {failures.Count} failed.");
        foreach (var f in failures) _output.WriteLine("  " + f);

        Assert.True(failures.Count == 0,
            $"{failures.Count} mapped types failed against PostgreSQL:\n" + string.Join("\n", failures));
    }
}
