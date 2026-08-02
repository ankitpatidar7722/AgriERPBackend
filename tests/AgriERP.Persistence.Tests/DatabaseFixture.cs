using AgriERP.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AgriERP.Persistence.Tests;

/// <summary>
/// These are integration tests against the real AgriERP database, not unit
/// tests against an in-memory provider - and deliberately so.
///
/// The point of this suite is to catch drift between the EF model and the
/// schema created by database/scripts/. An in-memory or SQLite provider would
/// happily accept a misnamed column, a wrong precision or a computed column
/// EF thinks it can write, and the failure would surface in itemion instead.
/// Only real SQL Server can refute those.
/// </summary>
public class DatabaseFixture : IDisposable
{
    public AgriErpDbContext Context { get; }
    public string ConnectionString { get; }

    public DatabaseFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.test.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        ConnectionString = configuration.GetConnectionString("AgriERP")
            ?? throw new InvalidOperationException("Connection string 'AgriERP' not configured.");

        var options = new DbContextOptionsBuilder<AgriErpDbContext>()
            .UseSqlServer(ConnectionString)
            .EnableSensitiveDataLogging()
            .Options;

        Context = new AgriErpDbContext(options);
    }

    public AgriErpDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AgriErpDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AgriErpDbContext(options);
    }

    public void Dispose() => Context.Dispose();
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
