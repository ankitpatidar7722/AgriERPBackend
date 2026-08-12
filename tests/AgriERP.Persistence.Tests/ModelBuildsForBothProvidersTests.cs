using AgriERP.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgriERP.Persistence.Tests;

/// <summary>
/// Offline model-build checks - no database is contacted. Accessing Context.Model
/// forces OnModelCreating plus EF's model validation, so a provider-specific
/// mapping mistake (a store type PostgreSQL does not know, a bad concurrency
/// token) fails here rather than at first query. Deliberately NOT in the
/// "Database" collection: these need no connection.
/// </summary>
public class ModelBuildsForBothProvidersTests
{
    [Fact]
    public void SqlServer_model_builds()
    {
        var options = new DbContextOptionsBuilder<AgriErpDbContext>()
            .UseSqlServer("Server=offline;Database=offline;Trusted_Connection=True;")
            .Options;

        using var context = new AgriErpDbContext(options);

        Assert.NotNull(context.Model);
    }

    [Fact]
    public void Postgres_model_builds()
    {
        var options = new DbContextOptionsBuilder<AgriErpDbContext>()
            .UseNpgsql("Host=offline;Database=offline;Username=offline;Password=offline")
            .Options;

        using var context = new AgriErpDbContext(options);

        Assert.NotNull(context.Model);
    }
}
