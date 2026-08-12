using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Dashboard;
using AgriERP.Domain.Enums;
using AgriERP.Persistence.Context;
using AgriERP.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgriERP.Persistence.Tests;

/// <summary>
/// Exercises the provider-agnostic services against the local PostgreSQL
/// "agrierp" database through EF + Npgsql - i.e. the real path the app takes,
/// hitting the ported PL/pgSQL functions. Everything runs inside a transaction
/// that is rolled back, so the database is left untouched.
/// </summary>
public class PostgresServicesTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=agrierp;Username=postgres;Password=postgres";

    private const string Seed = @"
INSERT INTO ""FinancialYears""(""FinancialYearId"",""YearCode"",""StartDate"",""EndDate"",""IsActive"") VALUES (1,'2025-26','2025-04-01','2026-03-31',true);
INSERT INTO ""NumberSeries""(""DocumentType"",""FinancialYearId"",""Prefix"",""Separator"",""IncludeYearCode"",""CurrentNumber"",""PaddingLength"") VALUES ('SalesInvoice',1,'INV','/',true,0,5);
INSERT INTO ""Units""(""UnitId"",""UnitCode"",""UnitName"") VALUES (1,'PCS','Pieces');
INSERT INTO ""GstSlabs""(""GstSlabId"",""SlabName"",""TotalRate"",""CgstRate"",""SgstRate"",""IgstRate"") VALUES (1,'18',18,9,9,18);
INSERT INTO ""ItemGroupMaster""(""ItemGroupId"",""ItemGroupCode"",""ItemGroupName"",""ItemCodePrefix"") VALUES (1,'GEN','General','GEN');
INSERT INTO ""ItemSubGroupMaster""(""ItemSubGroupId"",""ItemSubGroupCode"",""ItemSubGroupName"",""ItemGroupId"") VALUES (1,'SG','Sub',1);
INSERT INTO ""StorageLocations""(""LocationId"",""LocationCode"",""LocationName"",""LocationType"") VALUES (1,'L','Main','Rack');
INSERT INTO ""ItemMaster""(""ItemId"",""ItemCode"",""ItemName"",""ItemSubGroupId"",""UnitId"",""GstSlabId"",""ItemGroupId"",""AllowNegativeStock"") VALUES (1,'ITM1','Test Item',1,1,1,1,false);
INSERT INTO ""ItemBatches""(""BatchId"",""ItemId"",""BatchNumber"",""LocationId"",""InwardQty"",""OutwardQty"") VALUES (1,1,'B1',1,100,0);
INSERT INTO ""TransactionTypes""(""TransactionTypeId"",""TypeCode"",""TypeName"",""Direction"") VALUES (1,'IN','Inward',1),(2,'OUT','Outward',-1);";

    private static AgriErpDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AgriErpDbContext>().UseNpgsql(ConnectionString).Options;
        return new AgriErpDbContext(options);
    }

    private static StockMovement Movement(int typeId, decimal qty, string refType = "Test", long refId = 1)
        => new((StockTransactionTypeId)typeId, DateTime.Now, ItemId: 1, BatchId: 1, LocationId: 1,
               Quantity: qty, Rate: 10m, ReferenceType: refType, ReferenceId: refId,
               ReferenceDetailId: null, ReferenceNumber: "R1");

    [Fact]
    public async Task Numbering_posting_and_dashboard_work_through_ef_on_postgres()
    {
        await using var context = NewContext();
        if (!await context.Database.CanConnectAsync()) return;   // PG not available - skip

        await using var tx = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync(Seed);

        var numbers = new DocumentNumberService(context);
        var stock = new StockPostingService(context, new StubUser());
        var dashboard = new DashboardService(context);

        // Document numbering: consecutive + formatted.
        Assert.Equal("INV/2025-26/00001", await numbers.NextAsync("SalesInvoice"));
        Assert.Equal("INV/2025-26/00002", await numbers.NextAsync("SalesInvoice"));
        Assert.Equal("INV/2025-26/00003", await numbers.PeekNextAsync("SalesInvoice"));   // peek does not consume

        // Stock posting: inward 20 lifts the batch 100 -> 120.
        var txnId = await stock.PostAsync(Movement(1, 20m));
        Assert.True(txnId > 0);
        var qty = await context.ItemBatches.Where(b => b.BatchId == 1).Select(b => b.CurrentQty).SingleAsync();
        Assert.Equal(120m, qty);

        // Reversal puts it back.
        await stock.PostAsync(Movement(2, 30m, "RevMe", 77));
        Assert.Equal(1, await stock.ReverseDocumentAsync("RevMe", 77));
        qty = await context.ItemBatches.Where(b => b.BatchId == 1).Select(b => b.CurrentQty).SingleAsync();
        Assert.Equal(120m, qty);

        // Dashboard: six blocks read via refcursors without error.
        var dto = await dashboard.GetAsync(DateTime.Today, topCount: 5, graphMonths: 6);
        Assert.NotNull(dto.Headline);
        Assert.NotNull(dto.MonthlyTrend);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Insufficient_stock_raises_business_rule_exception()
    {
        await using var context = NewContext();
        if (!await context.Database.CanConnectAsync()) return;

        await using var tx = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync(Seed);

        var stock = new StockPostingService(context, new StubUser());

        // Outward 500 against 100 on hand, AllowNegativeStock = false.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => stock.PostAsync(Movement(2, 500m)));
        Assert.Contains("Insufficient stock", ex.Message);

        await tx.RollbackAsync();
    }

    private sealed class StubUser : ICurrentUserService
    {
        public int? UserId => null;
        public string? UserName => null;
        public string? FullName => null;
        public string? RoleName => null;
        public bool IsAuthenticated => false;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
        public string? IpAddress => null;
        public bool HasPermission(string permission) => false;
    }
}
