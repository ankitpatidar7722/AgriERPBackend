using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Services;
using AgriERP.Application.Features.Auth;
using AgriERP.Application.Features.Masters;
using AgriERP.Application.Features.Modules;
using AgriERP.Application.Features.Payments;
using AgriERP.Application.Features.Items;
using AgriERP.Application.Features.Purchases;
using AgriERP.Application.Features.Reports;
using AgriERP.Application.Features.Sales;
using AgriERP.Application.Features.Stock;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AgriERP.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // AutoMapper 15 replaced the AddAutoMapper(Assembly[]) overload with a
        // configuration action. Pinned at 15.1.3 or later deliberately:
        // everything below 15.1.1 carries CVE-2026-32933, an uncontrolled
        // recursion in nested-object mapping that kills the whole process
        // rather than failing one request.
        services.AddAutoMapper(configuration => configuration.AddMaps(assembly));
        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped);

        // Auth policy is bound once at startup rather than resolved per
        // request: it changes only on redeploy, and a singleton keeps the
        // pre-computed dummy hash from being rebuilt on every login attempt.
        var authOptions = new AuthOptions();
        configuration.GetSection("JwtSettings").Bind(authOptions);
        services.AddSingleton(authOptions);

        // Stateless calculators. Singleton because they hold no per-request
        // state and are called on every line of every document.
        services.AddSingleton<IGstCalculator, GstCalculator>();

        services.AddScoped<IBatchAllocator, BatchAllocator>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IItemSubGroupService, ItemSubGroupService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IShopService, ShopService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IItemGroupService, ItemGroupService>();
        services.AddScoped<IModuleService, ModuleService>();

        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}
