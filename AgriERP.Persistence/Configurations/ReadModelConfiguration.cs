using AgriERP.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriERP.Persistence.Configurations;

/*
 * Views are mapped HasNoKey().ToView(...). Two consequences worth knowing:
 *
 *   - EF will never attempt to INSERT/UPDATE/DELETE through them, so no
 *     accidental SaveChanges can corrupt a report source.
 *   - Keyless types are not change-tracked, which is exactly right for
 *     reporting reads and also makes them noticeably cheaper.
 */

public class ItemStockViewConfiguration : IEntityTypeConfiguration<ItemStockView>
{
    public void Configure(EntityTypeBuilder<ItemStockView> b)
    {
        b.HasNoKey().ToView("vw_ItemStock");
        b.Property(x => x.CurrentStock).AsQuantity();
        b.Property(x => x.MinStockLevel).AsQuantity();
        b.Property(x => x.MaxStockLevel).AsQuantity();
        b.Property(x => x.ReorderLevel).AsQuantity();
        b.Property(x => x.PurchaseRate).AsRate();
        b.Property(x => x.SellingRate).AsRate();
        b.Property(x => x.Mrp).AsRate();
        b.Property(x => x.StockValueAtCost).AsAmount();
        b.Property(x => x.StockValueAtMrp).AsAmount();
        b.Property(x => x.NearestExpiryDate).AsNullableDate();
    }
}

public class BatchStockViewConfiguration : IEntityTypeConfiguration<BatchStockView>
{
    public void Configure(EntityTypeBuilder<BatchStockView> b)
    {
        b.HasNoKey().ToView("vw_BatchStock");
        b.Property(x => x.InwardQty).AsQuantity();
        b.Property(x => x.OutwardQty).AsQuantity();
        b.Property(x => x.CurrentQty).AsQuantity();
        b.Property(x => x.PurchaseRate).AsRate();
        b.Property(x => x.SellingRate).AsRate();
        b.Property(x => x.Mrp).AsRate();
        b.Property(x => x.StockValueAtCost).AsAmount();
        b.Property(x => x.ManufacturingDate).AsNullableDate();
        b.Property(x => x.ExpiryDate).AsNullableDate();
    }
}

public class StockLedgerViewConfiguration : IEntityTypeConfiguration<StockLedgerView>
{
    public void Configure(EntityTypeBuilder<StockLedgerView> b)
    {
        b.HasNoKey().ToView("vw_StockLedger");
        b.Property(x => x.TransactionDate).AsTimestamp();
        b.Property(x => x.ExpiryDate).AsNullableDate();
        b.Property(x => x.InwardQty).AsQuantity();
        b.Property(x => x.OutwardQty).AsQuantity();
        b.Property(x => x.SignedQuantity).AsQuantity();
        b.Property(x => x.RunningBalance).AsQuantity();
        b.Property(x => x.BatchRunningBalance).AsQuantity();
        b.Property(x => x.Rate).AsRate();
        b.Property(x => x.Value).AsAmount();
        b.Property(x => x.CreatedAt).AsTimestamp();
    }
}

public class CustomerOutstandingViewConfiguration : IEntityTypeConfiguration<CustomerOutstandingView>
{
    public void Configure(EntityTypeBuilder<CustomerOutstandingView> b)
    {
        b.HasNoKey().ToView("vw_CustomerOutstanding");
        b.Property(x => x.CreditLimit).AsAmount();
        b.Property(x => x.OpeningBalance).AsAmount();
        b.Property(x => x.TotalBilled).AsAmount();
        b.Property(x => x.TotalReceived).AsAmount();
        b.Property(x => x.UnpaidBalance).AsAmount();
        b.Property(x => x.AdjustedReturns).AsAmount();
        b.Property(x => x.OnAccountAdvance).AsAmount();
        b.Property(x => x.OutstandingAmount).AsAmount();
        b.Property(x => x.LastInvoiceDate).AsNullableDate();
        b.Property(x => x.OldestUnpaidDate).AsNullableDate();
    }
}

public class CustomerLedgerViewConfiguration : IEntityTypeConfiguration<CustomerLedgerView>
{
    public void Configure(EntityTypeBuilder<CustomerLedgerView> b)
    {
        b.HasNoKey().ToView("vw_CustomerLedger");
        b.Property(x => x.TransactionDate).AsDate();
        b.Property(x => x.Debit).AsAmount();
        b.Property(x => x.Credit).AsAmount();
        b.Property(x => x.RunningBalance).AsAmount();
    }
}

public class SupplierOutstandingViewConfiguration : IEntityTypeConfiguration<SupplierOutstandingView>
{
    public void Configure(EntityTypeBuilder<SupplierOutstandingView> b)
    {
        b.HasNoKey().ToView("vw_SupplierOutstanding");
        b.Property(x => x.CreditLimit).AsAmount();
        b.Property(x => x.OpeningBalance).AsAmount();
        b.Property(x => x.TotalPurchased).AsAmount();
        b.Property(x => x.TotalPaid).AsAmount();
        b.Property(x => x.UnpaidBalance).AsAmount();
        b.Property(x => x.ReturnValue).AsAmount();
        b.Property(x => x.OnAccountAdvance).AsAmount();
        b.Property(x => x.OutstandingAmount).AsAmount();
        b.Property(x => x.LastPurchaseDate).AsNullableDate();
        b.Property(x => x.OldestUnpaidDate).AsNullableDate();
        b.Property(x => x.NextDueDate).AsNullableDate();
    }
}

public class ItemSubGroupWiseStockViewConfiguration : IEntityTypeConfiguration<ItemSubGroupWiseStockView>
{
    public void Configure(EntityTypeBuilder<ItemSubGroupWiseStockView> b)
    {
        b.HasNoKey().ToView("vw_ItemSubGroupWiseStock");
        b.Property(x => x.TotalQuantity).AsQuantity();
        b.Property(x => x.StockValueAtCost).AsAmount();
        b.Property(x => x.StockValueAtMrp).AsAmount();
    }
}

public class DailySalesSummaryViewConfiguration : IEntityTypeConfiguration<DailySalesSummaryView>
{
    public void Configure(EntityTypeBuilder<DailySalesSummaryView> b)
    {
        b.HasNoKey().ToView("vw_DailySalesSummary");
        b.Property(x => x.InvoiceDate).AsDate();
        b.Property(x => x.TaxableAmount).AsAmount();
        b.Property(x => x.TaxAmount).AsAmount();
        b.Property(x => x.TotalSales).AsAmount();
        b.Property(x => x.TotalCost).AsAmount();
        b.Property(x => x.GrossProfit).AsAmount();
        b.Property(x => x.AmountReceived).AsAmount();
        b.Property(x => x.CreditGiven).AsAmount();
    }
}

public class DailyPurchaseSummaryViewConfiguration : IEntityTypeConfiguration<DailyPurchaseSummaryView>
{
    public void Configure(EntityTypeBuilder<DailyPurchaseSummaryView> b)
    {
        b.HasNoKey().ToView("vw_DailyPurchaseSummary");
        b.Property(x => x.PurchaseDate).AsDate();
        b.Property(x => x.TaxableAmount).AsAmount();
        b.Property(x => x.TaxAmount).AsAmount();
        b.Property(x => x.TotalPurchase).AsAmount();
        b.Property(x => x.AmountPaid).AsAmount();
        b.Property(x => x.AmountDue).AsAmount();
    }
}
