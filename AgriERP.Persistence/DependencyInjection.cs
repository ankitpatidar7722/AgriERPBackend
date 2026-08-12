using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Dashboard;
using AgriERP.Application.Features.Modules;
using AgriERP.Persistence.Context;
using AgriERP.Persistence.Interceptors;
using AgriERP.Persistence.Repositories;
using AgriERP.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgriERP.Persistence;

public static class DependencyInjection
{
    public const string ConnectionStringName = "AgriERP";

    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' was not found. " +
                "Add it to appsettings.json or set ConnectionStrings__AgriERP.");

        services.AddScoped<AuditableEntityInterceptor>();

        // SqlServer stays the default, so existing deployments and the numbered
        // T-SQL scripts keep working unchanged; PostgreSQL is opt-in via config.
        var provider = configuration.GetValue<string>("Database:Provider") ?? "SqlServer";

        services.AddDbContext<AgriErpDbContext>((serviceProvider, options) =>
        {
            switch (provider.Trim().ToLowerInvariant())
            {
                case "postgresql":
                case "postgres":
                case "npgsql":
                    // Audit timestamps are DateTime.UtcNow (Kind=Utc) and the
                    // PostgreSQL columns are timestamp(3) (without time zone).
                    // Npgsql 6+ otherwise refuses to write a Kind=Utc DateTime to a
                    // 'timestamp without time zone' column; this restores storing the
                    // UTC value as-is. Set before the data source is built.
                    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

                    options.UseNpgsql(connectionString, npgsql =>
                    {
                        npgsql.CommandTimeout(60);

                        // Same transient-retry contract as SQL Server: a retry
                        // replays the whole unit of work, never half an invoice.
                        npgsql.EnableRetryOnFailure(
                            maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(5),
                            errorCodesToAdd: null);
                    });
                    break;

                case "sqlserver":
                case "mssql":
                    options.UseSqlServer(connectionString, sql =>
                    {
                        sql.CommandTimeout(60);

                        // Retries around transient SQL failures. Transactions opened
                        // through IUnitOfWork.ExecuteInTransactionAsync go via the
                        // execution strategy, so a retry replays the whole unit rather
                        // than half of an invoice.
                        sql.EnableRetryOnFailure(
                            maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(5),
                            errorNumbersToAdd: null);
                    });
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported Database:Provider '{provider}'. " +
                        "Use 'SqlServer' (default) or 'PostgreSQL'.");
            }

            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Named rather than generic: ModuleMaster has one query - the live
        // menu in display order - and every caller must get the same one.
        services.AddScoped<IModuleRepository, ModuleRepository>();

        services.AddScoped<IDocumentNumberService, DocumentNumberService>();

        // These two live in Persistence rather than Application because they
        // call stored procedures directly - the concurrency-safe numbering and
        // stock posting, and the six-result-set dashboard read.
        services.AddScoped<IStockPostingService, StockPostingService>();
        services.AddScoped<IDashboardService, DashboardService>();

        return services;
    }
}
