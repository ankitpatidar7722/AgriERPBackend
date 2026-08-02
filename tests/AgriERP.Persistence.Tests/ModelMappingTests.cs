using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Reflection;

namespace AgriERP.Persistence.Tests;

[Collection("Database")]
public class ModelMappingTests
{
    private readonly DatabaseFixture _fixture;

    public ModelMappingTests(DatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Queries every mapped table and view. A misnamed column, a wrong type or
    /// a property EF thinks exists but the database does not, all surface here
    /// as a SqlException rather than at the billing counter.
    /// </summary>
    [Fact]
    public async Task Every_mapped_type_can_be_queried_against_the_real_database()
    {
        var context = _fixture.Context;
        var probe = typeof(ModelMappingTests)
            .GetMethod(nameof(ProbeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        var failures = new List<string>();
        var entityTypes = context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .OrderBy(e => e.ClrType.Name)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            try
            {
                var task = (Task)probe.MakeGenericMethod(entityType.ClrType)
                                      .Invoke(null, new object[] { context })!;
                await task;
            }
            catch (Exception ex)
            {
                var root = ex.GetBaseException();
                failures.Add($"{entityType.ClrType.Name} -> {root.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {entityTypes.Count} mapped types failed:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));

        // 56 tables + 9 views (vw_CustomerLedger added). If this number drops, a DbSet was lost.
        // The three added by the item-group work are ItemGroup, ItemGroupField
        // and ItemMasterDetail; the voucher work added VoucherMaster; the
        // requisition work added PurchaseRequisition + PurchaseRequisitionDetail;
        // the shop/warehouse masters added ShopMaster, WarehouseMaster and WarehouseBin.
        Assert.Equal(65, entityTypes.Count);
    }

    private static async Task ProbeAsync<T>(DbContext context) where T : class
        => await context.Set<T>().Take(1).ToListAsync();

    /// <summary>
    /// Computed columns must be store-generated. If EF ever believed it owned
    /// one of these, it would try to INSERT into a computed column and every
    /// save on that table would fail - or worse, a total would be written by
    /// the application and drift from the lines that produced it.
    /// </summary>
    [Fact]
    public void Computed_columns_are_never_written_by_EF()
    {
        var context = _fixture.Context;

        var computed = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.GetComputedColumnSql() is not null)
            .ToList();

        // 40 persisted computed columns exist in the schema; every one of them
        // must be mapped, or the model has silently lost track of one.
        // (PurchaseRequisitionDetail.PendingQty was the most recent addition.)
        Assert.Equal(40, computed.Count);

        var writable = computed
            .Where(p => p.ValueGenerated != ValueGenerated.OnAddOrUpdate)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToList();

        Assert.True(writable.Count == 0,
            "These computed columns are not marked store-generated: " + string.Join(", ", writable));
    }

    /// <summary>
    /// Every mutable table carries a ROWVERSION so two users editing the same
    /// row produce a clean conflict instead of one silently overwriting the other.
    /// </summary>
    [Fact]
    public void RowVersion_columns_are_concurrency_tokens()
    {
        var context = _fixture.Context;

        var rowVersions = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.Name == "RowVersion")
            .ToList();

        Assert.NotEmpty(rowVersions);

        var notTokens = rowVersions
            .Where(p => !p.IsConcurrencyToken)
            .Select(p => p.DeclaringType.ClrType.Name)
            .ToList();

        Assert.True(notTokens.Count == 0,
            "RowVersion is not a concurrency token on: " + string.Join(", ", notTokens));
    }

    /// <summary>
    /// Decimal precision is not decoration. A rate stored at two decimals
    /// rounds 380.4550 to 380.46 on every consignment, and the error compounds
    /// through valuation into profit.
    /// </summary>
    [Fact]
    public void Decimal_precision_matches_the_schema()
    {
        var context = _fixture.Context;

        var lowPrecision = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
            .Where(p => (p.GetPrecision() ?? 0) < 18 && (p.GetPrecision() ?? 0) != 6)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name} " +
                         $"({p.GetPrecision()},{p.GetScale()})")
            .ToList();

        Assert.True(lowPrecision.Count == 0,
            "Decimal properties with unexpected precision: " + string.Join(", ", lowPrecision));
    }

    /// <summary>
    /// Enums are stored as readable strings, matching the CHECK constraints in
    /// the SQL scripts. If a member were renamed without updating the CHECK,
    /// the insert would be rejected - so this asserts the mapping is string,
    /// not int.
    /// </summary>
    [Fact]
    public void Enums_are_stored_as_strings()
    {
        var context = _fixture.Context;

        var saleType = context.Model.FindEntityType(typeof(Domain.Entities.Sales.Sale))!
            .FindProperty(nameof(Domain.Entities.Sales.Sale.SaleType))!;
        Assert.Equal(typeof(string), saleType.GetProviderClrType());

        var customerType = context.Model.FindEntityType(typeof(Customer))!
            .FindProperty(nameof(Customer.CustomerType))!;
        Assert.Equal(typeof(string), customerType.GetProviderClrType());

        var balanceType = context.Model.FindEntityType(typeof(Supplier))!
            .FindProperty(nameof(Supplier.OpeningBalanceType))!;
        Assert.Equal(typeof(string), balanceType.GetProviderClrType());
    }

    /// <summary>
    /// The seed data the application cannot start without.
    /// </summary>
    [Fact]
    public async Task Reference_data_is_seeded()
    {
        var context = _fixture.Context;

        Assert.Equal(37, await context.States.CountAsync());
        Assert.Equal(5,  await context.GstSlabs.CountAsync());
        // Filtered on IsDeleted. Counting every row would make this assertion
        // fail whenever anything else in the suite soft-deletes a itemSubGroup -
        // which is the correct behaviour of a soft delete, not a seeding fault.
        Assert.Equal(16, await context.ItemSubGroups.CountAsync(c => !c.IsDeleted));  // 12 parents + 4 seed types
        Assert.Equal(4,  await context.Roles.CountAsync());
        Assert.True(await context.Permissions.CountAsync() >= 70);
        Assert.Equal(1,  await context.CompanyProfile.CountAsync());
        Assert.Equal(1,  await context.FinancialYears.CountAsync(f => f.IsActive));

        // The seed sub-itemSubGroups must actually hang off "Seeds".
        var seeds = await context.ItemSubGroups.SingleAsync(c => c.ItemSubGroupCode == "SEED" && !c.IsDeleted);
        var children = await context.ItemSubGroups
            .Where(c => c.ParentItemSubGroupId == seeds.ItemSubGroupId && !c.IsDeleted)
            .CountAsync();
        Assert.Equal(4, children);
    }

    /// <summary>
    /// End-to-end write: create a item and a batch through EF, read the
    /// results back through the reporting view, then remove them. Proves the
    /// enum conversions, decimal precision and computed columns all round-trip
    /// against real SQL Server.
    /// </summary>
    [Fact]
    public async Task Item_and_batch_round_trip_through_EF()
    {
        await using var context = _fixture.NewContext();

        // Clear residue from an interrupted previous run, so a single failure
        // does not poison every subsequent run with a duplicate-key error.
        await context.Database.ExecuteSqlRawAsync(
            """
            DELETE b FROM ItemBatches b
              INNER JOIN ItemMaster p ON p.ItemId = b.ItemId
              WHERE p.ItemCode LIKE 'ZZEF-%';
            DELETE FROM ItemMaster WHERE ItemCode LIKE 'ZZEF-%';
            """);

        var itemSubGroup = await context.ItemSubGroups.FirstAsync(c => c.ItemSubGroupCode == "INSEC");
        var unitBtl  = await context.Units.FirstAsync(u => u.UnitCode == "BTL");
        var unitMl   = await context.Units.FirstAsync(u => u.UnitCode == "ML");
        var gst18    = await context.GstSlabs.FirstAsync(g => g.TotalRate == 18.000m);
        var location = await context.StorageLocations.FirstAsync(l => l.IsDefault);

        var item = new Item
        {
            ItemCode    = "ZZEF-001",
            ItemName    = "EF Round Trip Test Item",
            TechnicalName  = "Imidacloprid 17.8% SL",
            // Taken from the sub-group rather than hardcoded - that is the rule
            // the application follows, so a bad group mapping fails here rather
            // than surfacing later as an item on the wrong entry form.
            ItemGroupId    = itemSubGroup.ItemGroupId,
            ItemSubGroupId     = itemSubGroup.ItemSubGroupId,
            UnitId         = unitBtl.UnitId,
            PackingSize    = 250m,
            PackingUnitId  = unitMl.UnitId,
            GstSlabId      = gst18.GstSlabId,
            PurchaseRate   = 380.4550m,       // four decimals must survive
            SellingRate    = 450.0000m,
            Mrp            = 495.0000m,
            MinStockLevel  = 10m,
            MaxStockLevel  = 100m
        };

        context.Items.Add(item);
        await context.SaveChangesAsync();

        ItemBatch? batch = null;
        try
        {
            batch = new ItemBatch
            {
                ItemId    = item.ItemId,
                BatchNumber  = "ZZEF-B1",
                LocationId   = location.LocationId,
                ExpiryDate   = new DateTime(2027, 12, 31),
                PurchaseRate = 380.4550m,
                Mrp          = 495.0000m,
                InwardQty    = 12.500m,
                OutwardQty   = 2.500m
            };

            context.ItemBatches.Add(batch);
            await context.SaveChangesAsync();

            // CurrentQty is computed by SQL Server, so it must come back
            // populated without EF ever having sent a value for it.
            await context.Entry(batch).ReloadAsync();
            Assert.Equal(10.000m, batch.CurrentQty);

            // Rate precision survived the round trip rather than rounding to 380.46.
            var saved = await context.Items.AsNoTracking()
                .SingleAsync(p => p.ItemId == item.ItemId);
            Assert.Equal(380.4550m, saved.PurchaseRate);

            // The reporting view rolls the batch up to item level.
            var stock = await context.ItemStock.AsNoTracking()
                .SingleAsync(s => s.ItemId == item.ItemId);
            Assert.Equal(10.000m, stock.CurrentStock);
            Assert.Equal(3804.55m, stock.StockValueAtCost);   // 10 * 380.4550

            // Stock sitting exactly ON the minimum is LowStock, not Normal:
            // the minimum is the reorder trigger, so hitting it must raise the
            // alert rather than wait for the next packet to be sold.
            Assert.Equal(10m, item.MinStockLevel);
            Assert.Equal("LowStock", stock.StockStatus);
        }
        finally
        {
            // Both deletes in ONE SaveChanges. Deleting the batch first and the
            // item second would leave the tracked Item.Batches collection
            // pointing at a detached child, which EF reports as a severed
            // required relationship. Batched together, it orders them correctly.
            if (batch is not null) context.ItemBatches.Remove(batch);
            context.Items.Remove(item);   // hard delete: test residue, not a business record
            await context.SaveChangesAsync();
        }
    }

    /// <summary>Views must be read-only - EF should refuse to track them for writes.</summary>
    [Fact]
    public void Reporting_views_are_keyless_and_read_only()
    {
        var context = _fixture.Context;

        var viewTypes = new[]
        {
            typeof(Domain.ReadModels.ItemStockView),
            typeof(Domain.ReadModels.BatchStockView),
            typeof(Domain.ReadModels.StockLedgerView),
            typeof(Domain.ReadModels.CustomerOutstandingView),
            typeof(Domain.ReadModels.SupplierOutstandingView),
            typeof(Domain.ReadModels.ItemSubGroupWiseStockView),
            typeof(Domain.ReadModels.DailySalesSummaryView),
            typeof(Domain.ReadModels.DailyPurchaseSummaryView)
        };

        foreach (var type in viewTypes)
        {
            var entityType = context.Model.FindEntityType(type);
            Assert.NotNull(entityType);
            Assert.Null(entityType!.FindPrimaryKey());
            Assert.NotNull(entityType.GetViewName());
        }
    }
}
