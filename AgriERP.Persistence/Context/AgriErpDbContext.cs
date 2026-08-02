using AgriERP.Domain.Entities.Finance;
using AgriERP.Domain.Entities.Inventory;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Entities.Purchases;
using AgriERP.Domain.Entities.Sales;
using AgriERP.Domain.Entities.Security;
using AgriERP.Domain.Entities.System;
using AgriERP.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace AgriERP.Persistence.Context;

/// <summary>
/// EF Core context for AgriERP.
///
/// THE SQL SCRIPTS ARE THE SOURCE OF TRUTH FOR THE SCHEMA.
/// ------------------------------------------------------
/// This model is configured to match database/scripts/ exactly, but it does
/// not generate it. Schema changes go through a new numbered script, not
/// through `dotnet ef migrations add`.
///
/// The reason is that the schema carries things EF migrations model poorly or
/// not at all: 39 persisted computed columns holding the money arithmetic, 66
/// CHECK constraints, filtered unique indexes, and six stored procedures.
/// Running migrations alongside hand-written scripts means two tools believing
/// they own the schema, and the first `Update-Database` on a live shop would
/// try to "fix" everything it did not author.
///
/// AgriErpModelVerification (in tests) queries every mapped table and view, so
/// a drift between this model and the database fails fast rather than at the
/// billing counter.
///
/// NO GLOBAL QUERY FILTERS ON SOFT DELETE - deliberately. A filter on
/// Item.IsDeleted would make Include(x => x.Item) return null for a sale
/// line whose item was later deleted, silently breaking the reprint of an
/// old invoice. Lists and lookups filter explicitly in the repositories;
/// historical documents always resolve their masters.
/// </summary>
public class AgriErpDbContext : DbContext
{
    public AgriErpDbContext(DbContextOptions<AgriErpDbContext> options) : base(options)
    {
    }

    // ---- sec ----------------------------------------------------------------
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<UserPasswordReset> UserPasswordResets => Set<UserPasswordReset>();

    // ---- mst ----------------------------------------------------------------
    public DbSet<State> States => Set<State>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<GstSlab> GstSlabs => Set<GstSlab>();
    public DbSet<HsnCode> HsnCodes => Set<HsnCode>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<VoucherMaster> Vouchers => Set<VoucherMaster>();
    public DbSet<ItemSubGroup> ItemSubGroups => Set<ItemSubGroup>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<ShopMaster> Shops => Set<ShopMaster>();
    public DbSet<WarehouseMaster> Warehouses => Set<WarehouseMaster>();
    public DbSet<WarehouseBin> WarehouseBins => Set<WarehouseBin>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ItemGroup> ItemGroups => Set<ItemGroup>();
    public DbSet<ItemGroupField> ItemGroupFields => Set<ItemGroupField>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemMasterDetail> ItemMasterDetails => Set<ItemMasterDetail>();
    public DbSet<ItemBatch> ItemBatches => Set<ItemBatch>();
    public DbSet<ItemImage> ItemImages => Set<ItemImage>();
    public DbSet<ItemPriceHistory> ItemPriceHistory => Set<ItemPriceHistory>();

    // ---- inv ----------------------------------------------------------------
    public DbSet<TransactionType> TransactionTypes => Set<TransactionType>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentDetail> StockAdjustmentDetails => Set<StockAdjustmentDetail>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferDetail> StockTransferDetails => Set<StockTransferDetail>();

    // ---- pur ----------------------------------------------------------------
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseRequisitionDetail> PurchaseRequisitionDetails => Set<PurchaseRequisitionDetail>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderDetail> PurchaseOrderDetails => Set<PurchaseOrderDetail>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseDetail> PurchaseDetails => Set<PurchaseDetail>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnDetail> PurchaseReturnDetails => Set<PurchaseReturnDetail>();

    // ---- sal ----------------------------------------------------------------
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SalesDetail> SalesDetails => Set<SalesDetail>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnDetail> SalesReturnDetails => Set<SalesReturnDetail>();

    // ---- fin ----------------------------------------------------------------
    public DbSet<PaymentMode> PaymentModes => Set<PaymentMode>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();

    // ---- app ----------------------------------------------------------------
    public DbSet<CompanyProfile> CompanyProfile => Set<CompanyProfile>();
    public DbSet<FinancialYear> FinancialYears => Set<FinancialYear>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ModuleMaster> Modules => Set<ModuleMaster>();

    // ---- read-only views ----------------------------------------------------
    public DbSet<ItemStockView> ItemStock => Set<ItemStockView>();
    public DbSet<BatchStockView> BatchStock => Set<BatchStockView>();
    public DbSet<StockLedgerView> StockLedger => Set<StockLedgerView>();
    public DbSet<CustomerOutstandingView> CustomerOutstanding => Set<CustomerOutstandingView>();
    public DbSet<CustomerLedgerView> CustomerLedger => Set<CustomerLedgerView>();
    public DbSet<SupplierOutstandingView> SupplierOutstanding => Set<SupplierOutstandingView>();
    public DbSet<ItemSubGroupWiseStockView> ItemSubGroupWiseStock => Set<ItemSubGroupWiseStockView>();
    public DbSet<DailySalesSummaryView> DailySalesSummary => Set<DailySalesSummaryView>();
    public DbSet<DailyPurchaseSummaryView> DailyPurchaseSummary => Set<DailyPurchaseSummaryView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Without this, EF picks DECIMAL(18,2) for every decimal and quietly
        // truncates rates to paise. Per-property overrides in the entity
        // configurations set the real precision; this is only the safety net.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }
}
