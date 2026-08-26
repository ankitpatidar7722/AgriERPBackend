using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Domain.Entities.Finance;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Purchases;
using AgriERP.Domain.Entities.Sales;
using AgriERP.Domain.Enums;
using AgriERP.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Reports;

/* ---------------------------- DTOs ---------------------------- */

public class DateRangeRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}

public class SalesReportRowDto
{
    public DateTime InvoiceDate { get; set; }
    public long InvoiceCount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal AmountReceived { get; set; }
    public decimal CreditGiven { get; set; }
    public int CashInvoiceCount { get; set; }
    public int CreditInvoiceCount { get; set; }
}

/// <summary>One customer's sales rolled up over a period: how much they bought, paid and still owe.</summary>
public class CustomerSalesRowDto
{
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Village { get; set; }
    public long InvoiceCount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal AmountReceived { get; set; }
    public decimal BalanceAmount { get; set; }
}

/// <summary>One supplier's purchases rolled up over a period: bought, paid and still payable.</summary>
public class SupplierPurchaseRowDto
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? City { get; set; }
    public long BillCount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalPurchase { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal BalanceAmount { get; set; }
}

public class PurchaseReportRowDto
{
    public DateTime PurchaseDate { get; set; }
    public long BillCount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalPurchase { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
}

public class ProfitReportRowDto
{
    public DateTime InvoiceDate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TotalCost { get; set; }
    public decimal GrossProfit { get; set; }

    /// <summary>Margin on revenue. Zero when there were no sales that day.</summary>
    public decimal MarginPercent { get; set; }
}

public class ItemProfitRowDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public decimal QuantitySold { get; set; }
    public decimal SalesValue { get; set; }
    public decimal CostValue { get; set; }
    public decimal Profit { get; set; }
    public decimal MarginPercent { get; set; }
}

public class GstSummaryRowDto
{
    public string? HsnCode { get; set; }
    public decimal GstPercent { get; set; }
    public bool IsInterState { get; set; }
    public long DocumentCount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalAmount { get; set; }
}

public class GstReturnDto
{
    public DateRangeRequest Period { get; set; } = new();
    public IReadOnlyList<GstSummaryRowDto> OutwardSupplies { get; set; } = Array.Empty<GstSummaryRowDto>();
    public IReadOnlyList<GstSummaryRowDto> InwardSupplies { get; set; } = Array.Empty<GstSummaryRowDto>();

    /// <summary>Output tax minus input credit. Positive means tax is payable.</summary>
    public decimal NetTaxPayable { get; set; }
}

public class StockValuationDto
{
    public long ItemCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal ValueAtCost { get; set; }
    public decimal ValueAtMrp { get; set; }
    public decimal PotentialMargin { get; set; }
}

public class CompanyWiseStockDto
{
    public int? CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public long ItemCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal StockValueAtCost { get; set; }
    public decimal StockValueAtMrp { get; set; }
}

/* ---------------------------- service ---------------------------- */

public class CashBookRowDto
{
    public DateTime Date { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    /// <summary>Sale / Receipt / Payment / Expense.</summary>
    public string Type { get; set; } = string.Empty;
    public string Particulars { get; set; } = string.Empty;
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal RunningBalance { get; set; }
}

/// <summary>Cash drawer for a period: opening + ins - outs = closing.</summary>
public class CashBookDto
{
    public decimal OpeningBalance { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<CashBookRowDto> Rows { get; set; } = Array.Empty<CashBookRowDto>();
}

/// <summary>One customer's unpaid balance split into age buckets (days overdue).</summary>
public class ReceivablesAgingRowDto
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Village { get; set; }
    public decimal Current { get; set; }      // 0-30 days
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
    public decimal Total { get; set; }
    public int OldestDays { get; set; }
}

public interface IReportService
{
    // inventory
    Task<IReadOnlyList<ItemStockView>> GetCurrentStockAsync(int? itemSubGroupId, int? companyId, CancellationToken ct = default);
    Task<IReadOnlyList<ItemStockView>> GetLowStockAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ItemStockView>> GetOutOfStockAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BatchStockView>> GetNearExpiryAsync(int withinDays, CancellationToken ct = default);
    Task<IReadOnlyList<BatchStockView>> GetExpiredAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ItemSubGroupWiseStockView>> GetItemSubGroupWiseStockAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CompanyWiseStockDto>> GetCompanyWiseStockAsync(CancellationToken ct = default);
    Task<StockValuationDto> GetStockValuationAsync(CancellationToken ct = default);

    // financial
    Task<IReadOnlyList<SalesReportRowDto>> GetSalesReportAsync(DateRangeRequest range, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerSalesRowDto>> GetSalesByCustomerAsync(DateRangeRequest range, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseReportRowDto>> GetPurchaseReportAsync(DateRangeRequest range, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierPurchaseRowDto>> GetPurchaseBySupplierAsync(DateRangeRequest range, CancellationToken ct = default);
    Task<IReadOnlyList<ProfitReportRowDto>> GetProfitReportAsync(DateRangeRequest range, CancellationToken ct = default);
    Task<IReadOnlyList<ItemProfitRowDto>> GetItemProfitAsync(DateRangeRequest range, int topCount, CancellationToken ct = default);
    Task<GstReturnDto> GetGstReturnAsync(DateRangeRequest range, CancellationToken ct = default);
    Task<CashBookDto> GetCashBookAsync(DateRangeRequest range, CancellationToken ct = default);
    Task<IReadOnlyList<ReceivablesAgingRowDto>> GetReceivablesAgingAsync(CancellationToken ct = default);
}

public class ReportService : IReportService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public ReportService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    /* ---------------------------- inventory ---------------------------- */

    public async Task<IReadOnlyList<ItemStockView>> GetCurrentStockAsync(
        int? itemSubGroupId, int? companyId, CancellationToken ct = default)
        => await _uow.Repository<ItemStockView>().Query()
            .Where(s => s.IsActive)
            .WhereIf(itemSubGroupId.HasValue, s => s.ItemSubGroupId == itemSubGroupId)
            .WhereIf(companyId.HasValue, s => s.CompanyId == companyId)
            .OrderBy(s => s.ItemSubGroupName).ThenBy(s => s.ItemName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ItemStockView>> GetLowStockAsync(CancellationToken ct = default)
        => await _uow.Repository<ItemStockView>().Query()
            .Where(s => s.IsActive && s.StockStatus == "LowStock")
            // Deepest shortfall first: that is the reorder list in priority order.
            .OrderBy(s => s.CurrentStock - s.MinStockLevel)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ItemStockView>> GetOutOfStockAsync(CancellationToken ct = default)
        => await _uow.Repository<ItemStockView>().Query()
            .Where(s => s.IsActive && s.CurrentStock <= 0)
            .OrderBy(s => s.ItemSubGroupName).ThenBy(s => s.ItemName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BatchStockView>> GetNearExpiryAsync(
        int withinDays, CancellationToken ct = default)
    {
        var cutoff = _clock.Today.AddDays(withinDays);

        return await _uow.Repository<BatchStockView>().Query()
            .Where(b => b.CurrentQty > 0
                        && b.ExpiryDate != null
                        // Already-expired stock belongs on the expired report,
                        // where the action is a write-off rather than a push to sell.
                        && b.ExpiryDate >= _clock.Today
                        && b.ExpiryDate <= cutoff)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BatchStockView>> GetExpiredAsync(CancellationToken ct = default)
        => await _uow.Repository<BatchStockView>().Query()
            .Where(b => b.CurrentQty > 0 && b.ExpiryDate != null && b.ExpiryDate < _clock.Today)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ItemSubGroupWiseStockView>> GetItemSubGroupWiseStockAsync(CancellationToken ct = default)
        => await _uow.Repository<ItemSubGroupWiseStockView>().Query()
            .Where(c => c.ItemCount > 0)
            .OrderByDescending(c => c.StockValueAtCost)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CompanyWiseStockDto>> GetCompanyWiseStockAsync(CancellationToken ct = default)
        // Grouped from vw_ItemStock rather than reading vw_CompanyWiseStock,
        // so items with no manufacturer set still appear under "Unbranded"
        // instead of vanishing from the total.
        => await _uow.Repository<ItemStockView>().Query()
            .Where(s => s.IsActive)
            .GroupBy(s => new { s.CompanyId, s.CompanyName })
            .Select(g => new CompanyWiseStockDto
            {
                CompanyId        = g.Key.CompanyId,
                CompanyName      = g.Key.CompanyName ?? "Unbranded",
                ItemCount     = g.Count(),
                TotalQuantity    = g.Sum(s => s.CurrentStock),
                StockValueAtCost = g.Sum(s => s.StockValueAtCost),
                StockValueAtMrp  = g.Sum(s => s.StockValueAtMrp)
            })
            .OrderByDescending(c => c.StockValueAtCost)
            .ToListAsync(ct);

    public async Task<StockValuationDto> GetStockValuationAsync(CancellationToken ct = default)
    {
        var totals = await _uow.Repository<ItemStockView>().Query()
            .Where(s => s.IsActive && s.CurrentStock > 0)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ItemCount  = g.LongCount(),
                TotalQuantity = g.Sum(s => s.CurrentStock),
                ValueAtCost   = g.Sum(s => s.StockValueAtCost),
                ValueAtMrp    = g.Sum(s => s.StockValueAtMrp)
            })
            .FirstOrDefaultAsync(ct);

        return new StockValuationDto
        {
            ItemCount    = totals?.ItemCount ?? 0,
            TotalQuantity   = totals?.TotalQuantity ?? 0m,
            ValueAtCost     = totals?.ValueAtCost ?? 0m,
            ValueAtMrp      = totals?.ValueAtMrp ?? 0m,
            PotentialMargin = (totals?.ValueAtMrp ?? 0m) - (totals?.ValueAtCost ?? 0m)
        };
    }

    /* ---------------------------- financial ---------------------------- */

    public async Task<IReadOnlyList<SalesReportRowDto>> GetSalesReportAsync(
        DateRangeRequest range, CancellationToken ct = default)
        => await _uow.Repository<DailySalesSummaryView>().Query()
            .Where(s => s.InvoiceDate >= range.FromDate.Date && s.InvoiceDate <= range.ToDate.Date)
            .OrderBy(s => s.InvoiceDate)
            .Select(s => new SalesReportRowDto
            {
                InvoiceDate        = s.InvoiceDate,
                InvoiceCount       = s.InvoiceCount,
                TaxableAmount      = s.TaxableAmount,
                TaxAmount          = s.TaxAmount,
                TotalSales         = s.TotalSales,
                AmountReceived     = s.AmountReceived,
                CreditGiven        = s.CreditGiven,
                CashInvoiceCount   = s.CashInvoiceCount,
                CreditInvoiceCount = s.CreditInvoiceCount
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CustomerSalesRowDto>> GetSalesByCustomerAsync(
        DateRangeRequest range, CancellationToken ct = default)
    {
        // Roll every posted invoice up to its customer. Walk-in (no customerId)
        // bills all fall into one "Walk-in / Cash" bucket.
        var grouped = await _uow.Repository<Sale>().Query()
            .Where(s => s.Status == DocumentStatus.Posted
                     && s.InvoiceDate >= range.FromDate.Date && s.InvoiceDate <= range.ToDate.Date)
            .GroupBy(s => s.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                InvoiceCount = g.LongCount(),
                TaxableAmount = g.Sum(x => x.TaxableAmount),
                TaxAmount = g.Sum(x => x.CgstAmount + x.SgstAmount + x.IgstAmount),
                TotalSales = g.Sum(x => x.GrandTotal),
                AmountReceived = g.Sum(x => x.ReceivedAmount),
                BalanceAmount = g.Sum(x => x.BalanceAmount)
            })
            .ToListAsync(ct);

        var ids = grouped.Where(x => x.CustomerId != null).Select(x => x.CustomerId!.Value).ToList();
        var names = await _uow.Repository<Customer>().Query()
            .Where(c => ids.Contains(c.CustomerId))
            .Select(c => new { c.CustomerId, c.CustomerName, c.Village })
            .ToListAsync(ct);
        var nameMap = names.ToDictionary(n => n.CustomerId);

        return grouped
            .Select(x =>
            {
                nameMap.TryGetValue(x.CustomerId ?? 0, out var c);
                return new CustomerSalesRowDto
                {
                    CustomerId = x.CustomerId,
                    CustomerName = c?.CustomerName ?? "Walk-in / Cash",
                    Village = c?.Village,
                    InvoiceCount = x.InvoiceCount,
                    TaxableAmount = x.TaxableAmount,
                    TaxAmount = x.TaxAmount,
                    TotalSales = x.TotalSales,
                    AmountReceived = x.AmountReceived,
                    BalanceAmount = x.BalanceAmount
                };
            })
            .OrderByDescending(r => r.TotalSales)
            .ToList();
    }

    public async Task<IReadOnlyList<ReceivablesAgingRowDto>> GetReceivablesAgingAsync(
        CancellationToken ct = default)
    {
        var today = _clock.UtcNow.Date;

        // One row per still-unpaid posted invoice; BalanceAmount is authoritative.
        var invoices = await _uow.Repository<Sale>().Query()
            .Where(s => s.Status == DocumentStatus.Posted && s.CustomerId != null && s.BalanceAmount > 0)
            .Select(s => new { CustomerId = s.CustomerId!.Value, s.InvoiceDate, s.DueDate, s.BalanceAmount })
            .ToListAsync(ct);

        if (invoices.Count == 0) return Array.Empty<ReceivablesAgingRowDto>();

        var ids = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var names = await _uow.Repository<Customer>().Query()
            .Where(c => ids.Contains(c.CustomerId))
            .Select(c => new { c.CustomerId, c.CustomerName, c.Village })
            .ToListAsync(ct);
        var nameMap = names.ToDictionary(n => n.CustomerId);

        return invoices
            .GroupBy(i => i.CustomerId)
            .Select(g =>
            {
                nameMap.TryGetValue(g.Key, out var c);
                var row = new ReceivablesAgingRowDto
                {
                    CustomerId = g.Key,
                    CustomerName = c?.CustomerName ?? $"#{g.Key}",
                    Village = c?.Village,
                };
                foreach (var inv in g)
                {
                    // Age from the due date if the bill has one, else the invoice date.
                    var age = (today - (inv.DueDate?.Date ?? inv.InvoiceDate.Date)).Days;
                    if (age > row.OldestDays) row.OldestDays = age;
                    if (age <= 30) row.Current += inv.BalanceAmount;
                    else if (age <= 60) row.Days31To60 += inv.BalanceAmount;
                    else if (age <= 90) row.Days61To90 += inv.BalanceAmount;
                    else row.Days90Plus += inv.BalanceAmount;
                }
                row.Total = row.Current + row.Days31To60 + row.Days61To90 + row.Days90Plus;
                return row;
            })
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    public async Task<CashBookDto> GetCashBookAsync(DateRangeRequest range, CancellationToken ct = default)
    {
        var from = range.FromDate.Date;
        var to = range.ToDate.Date;

        // "Cash" is a payment mode; its id is identity-generated, so resolve by code.
        var cashModeId = await _uow.Repository<PaymentMode>().Query()
            .Where(m => m.ModeCode == "CASH")
            .Select(m => (int?)m.PaymentModeId)
            .FirstOrDefaultAsync(ct);
        if (cashModeId is null) return new CashBookDto();

        var rows = new List<CashBookRowDto>();

        // --- Cash receipts and payments (the Payments ledger) ---
        var payments = await _uow.Repository<Payment>().Query()
            .Where(p => p.PaymentModeId == cashModeId && p.Status == PaymentRecordStatus.Posted
                     && p.PaymentDate >= from && p.PaymentDate <= to)
            .Select(p => new
            {
                p.PaymentDate, p.VoucherNumber, p.PaymentType, p.Amount,
                Party = p.Customer != null ? p.Customer.CustomerName
                      : p.Supplier != null ? p.Supplier.SupplierName : ""
            })
            .ToListAsync(ct);
        foreach (var p in payments)
            rows.Add(new CashBookRowDto
            {
                Date = p.PaymentDate,
                VoucherNumber = p.VoucherNumber,
                Type = p.PaymentType == PaymentDirection.Receipt ? "Receipt" : "Payment",
                Particulars = p.Party,
                CashIn = p.PaymentType == PaymentDirection.Receipt ? p.Amount : 0m,
                CashOut = p.PaymentType == PaymentDirection.Payment ? p.Amount : 0m
            });

        // --- Cash expenses ---
        var expenses = await _uow.Repository<Expense>().Query()
            .Where(e => e.PaymentModeId == cashModeId && e.Status == PaymentRecordStatus.Posted
                     && e.ExpenseDate >= from && e.ExpenseDate <= to)
            .Select(e => new { e.ExpenseDate, e.VoucherNumber, Category = e.ExpenseCategory!.CategoryName, e.TotalAmount })
            .ToListAsync(ct);
        foreach (var e in expenses)
            rows.Add(new CashBookRowDto
            {
                Date = e.ExpenseDate, VoucherNumber = e.VoucherNumber, Type = "Expense",
                Particulars = e.Category, CashOut = e.TotalAmount
            });

        // --- Cash collected at the counter. A sale's tender split names the mode;
        // a plain cash sale is entered with no tender rows, so its ReceivedAmount
        // is treated as the cash collected. ---
        var salesInRange = await _uow.Repository<Sale>().Query()
            .Where(s => s.Status == DocumentStatus.Posted && s.InvoiceDate >= from && s.InvoiceDate <= to)
            .Select(s => new
            {
                s.InvoiceDate, s.InvoiceNumber, s.ReceivedAmount,
                CustomerName = s.Customer != null ? s.Customer.CustomerName : "Walk-in / Cash",
                Tenders = s.Payments.Select(sp => new { sp.PaymentModeId, sp.Amount }).ToList()
            })
            .ToListAsync(ct);
        foreach (var s in salesInRange)
        {
            var cashIn = s.Tenders.Count > 0
                ? s.Tenders.Where(tp => tp.PaymentModeId == cashModeId).Sum(tp => tp.Amount)
                : s.ReceivedAmount;
            if (cashIn > 0)
                rows.Add(new CashBookRowDto
                {
                    Date = s.InvoiceDate, VoucherNumber = s.InvoiceNumber, Type = "Sale",
                    Particulars = s.CustomerName, CashIn = cashIn
                });
        }

        // --- Opening balance: net cash before the period (DB-side aggregates). ---
        var openReceipts = await _uow.Repository<Payment>().Query()
            .Where(p => p.PaymentModeId == cashModeId && p.Status == PaymentRecordStatus.Posted
                     && p.PaymentType == PaymentDirection.Receipt && p.PaymentDate < from)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var openPayments = await _uow.Repository<Payment>().Query()
            .Where(p => p.PaymentModeId == cashModeId && p.Status == PaymentRecordStatus.Posted
                     && p.PaymentType == PaymentDirection.Payment && p.PaymentDate < from)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var openExpenses = await _uow.Repository<Expense>().Query()
            .Where(e => e.PaymentModeId == cashModeId && e.Status == PaymentRecordStatus.Posted
                     && e.ExpenseDate < from)
            .SumAsync(e => (decimal?)e.TotalAmount, ct) ?? 0m;
        var openSaleTenders = await _uow.Repository<Sale>().Query()
            .Where(s => s.Status == DocumentStatus.Posted && s.InvoiceDate < from)
            .SelectMany(s => s.Payments.Where(sp => sp.PaymentModeId == cashModeId))
            .SumAsync(sp => (decimal?)sp.Amount, ct) ?? 0m;
        var openPlainSales = await _uow.Repository<Sale>().Query()
            .Where(s => s.Status == DocumentStatus.Posted && s.InvoiceDate < from
                     && s.ReceivedAmount > 0 && !s.Payments.Any())
            .SumAsync(s => (decimal?)s.ReceivedAmount, ct) ?? 0m;
        var opening = openReceipts + openSaleTenders + openPlainSales - openPayments - openExpenses;

        var ordered = rows.OrderBy(r => r.Date).ThenBy(r => r.VoucherNumber).ToList();
        var run = opening;
        foreach (var r in ordered)
        {
            run += r.CashIn - r.CashOut;
            r.RunningBalance = run;
        }

        return new CashBookDto
        {
            OpeningBalance = opening,
            TotalIn = ordered.Sum(r => r.CashIn),
            TotalOut = ordered.Sum(r => r.CashOut),
            ClosingBalance = run,
            Rows = ordered
        };
    }

    public async Task<IReadOnlyList<PurchaseReportRowDto>> GetPurchaseReportAsync(
        DateRangeRequest range, CancellationToken ct = default)
        => await _uow.Repository<DailyPurchaseSummaryView>().Query()
            .Where(p => p.PurchaseDate >= range.FromDate.Date && p.PurchaseDate <= range.ToDate.Date)
            .OrderBy(p => p.PurchaseDate)
            .Select(p => new PurchaseReportRowDto
            {
                PurchaseDate  = p.PurchaseDate,
                BillCount     = p.BillCount,
                TaxableAmount = p.TaxableAmount,
                TaxAmount     = p.TaxAmount,
                TotalPurchase = p.TotalPurchase,
                AmountPaid    = p.AmountPaid,
                AmountDue     = p.AmountDue
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SupplierPurchaseRowDto>> GetPurchaseBySupplierAsync(
        DateRangeRequest range, CancellationToken ct = default)
    {
        var grouped = await _uow.Repository<Purchase>().Query()
            .Where(p => p.Status == DocumentStatus.Posted
                     && p.PurchaseDate >= range.FromDate.Date && p.PurchaseDate <= range.ToDate.Date)
            .GroupBy(p => p.SupplierId)
            .Select(g => new
            {
                SupplierId = g.Key,
                BillCount = g.LongCount(),
                TaxableAmount = g.Sum(x => x.TaxableAmount),
                TaxAmount = g.Sum(x => x.CgstAmount + x.SgstAmount + x.IgstAmount),
                TotalPurchase = g.Sum(x => x.GrandTotal),
                AmountPaid = g.Sum(x => x.PaidAmount),
                BalanceAmount = g.Sum(x => x.BalanceAmount)
            })
            .ToListAsync(ct);

        var ids = grouped.Select(x => x.SupplierId).ToList();
        var suppliers = await _uow.Repository<Supplier>().Query()
            .Where(s => ids.Contains(s.SupplierId))
            .Select(s => new { s.SupplierId, s.SupplierName, s.City })
            .ToListAsync(ct);
        var map = suppliers.ToDictionary(s => s.SupplierId);

        return grouped
            .Select(x =>
            {
                map.TryGetValue(x.SupplierId, out var s);
                return new SupplierPurchaseRowDto
                {
                    SupplierId = x.SupplierId,
                    SupplierName = s?.SupplierName ?? "-",
                    City = s?.City,
                    BillCount = x.BillCount,
                    TaxableAmount = x.TaxableAmount,
                    TaxAmount = x.TaxAmount,
                    TotalPurchase = x.TotalPurchase,
                    AmountPaid = x.AmountPaid,
                    BalanceAmount = x.BalanceAmount
                };
            })
            .OrderByDescending(r => r.TotalPurchase)
            .ToList();
    }

    public async Task<IReadOnlyList<ProfitReportRowDto>> GetProfitReportAsync(
        DateRangeRequest range, CancellationToken ct = default)
    {
        var rows = await _uow.Repository<DailySalesSummaryView>().Query()
            .Where(s => s.InvoiceDate >= range.FromDate.Date && s.InvoiceDate <= range.ToDate.Date)
            .OrderBy(s => s.InvoiceDate)
            .Select(s => new
            {
                s.InvoiceDate, s.TaxableAmount, s.TotalCost, s.GrossProfit
            })
            .ToListAsync(ct);

        // Margin is computed after materialisation so the divide-by-zero guard
        // is plain C# rather than a CASE expression the provider has to translate.
        return rows.Select(r => new ProfitReportRowDto
        {
            InvoiceDate   = r.InvoiceDate,
            TaxableAmount = r.TaxableAmount,
            TotalCost     = r.TotalCost,
            GrossProfit   = r.GrossProfit,
            MarginPercent = r.TaxableAmount > 0
                ? Math.Round(r.GrossProfit / r.TaxableAmount * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m
        }).ToList();
    }

    public async Task<IReadOnlyList<ItemProfitRowDto>> GetItemProfitAsync(
        DateRangeRequest range, int topCount, CancellationToken ct = default)
    {
        var rows = await _uow.Repository<SalesDetail>().Query()
            .Where(d => d.Sale!.Status == DocumentStatus.Posted
                        && d.Sale.InvoiceDate >= range.FromDate.Date
                        && d.Sale.InvoiceDate <= range.ToDate.Date)
            .GroupBy(d => new { d.ItemId, d.Item!.ItemName, ItemSubGroupName = d.Item.ItemSubGroup!.ItemSubGroupName })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.ItemName,
                g.Key.ItemSubGroupName,
                QuantitySold = g.Sum(d => d.TotalQuantity),
                SalesValue   = g.Sum(d => d.TaxableAmount),
                CostValue    = g.Sum(d => d.CostAmount),
                Profit       = g.Sum(d => d.LineProfit)
            })
            .OrderByDescending(g => g.Profit)
            .Take(topCount)
            .ToListAsync(ct);

        return rows.Select(r => new ItemProfitRowDto
        {
            ItemId     = r.ItemId,
            ItemName   = r.ItemName,
            ItemSubGroupName  = r.ItemSubGroupName,
            QuantitySold  = r.QuantitySold,
            SalesValue    = r.SalesValue,
            CostValue     = r.CostValue,
            Profit        = r.Profit,
            MarginPercent = r.SalesValue > 0
                ? Math.Round(r.Profit / r.SalesValue * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m
        }).ToList();
    }

    public async Task<GstReturnDto> GetGstReturnAsync(DateRangeRequest range, CancellationToken ct = default)
    {
        var outward = await _uow.Repository<SalesDetail>().Query()
            .Where(d => d.Sale!.Status == DocumentStatus.Posted
                        && d.Sale.InvoiceDate >= range.FromDate.Date
                        && d.Sale.InvoiceDate <= range.ToDate.Date)
            .GroupBy(d => new { d.HsnCode, d.GstPercent, d.Sale!.IsInterState })
            .Select(g => new GstSummaryRowDto
            {
                HsnCode       = g.Key.HsnCode,
                GstPercent    = g.Key.GstPercent,
                IsInterState  = g.Key.IsInterState,
                DocumentCount = g.Select(d => d.SaleId).Distinct().LongCount(),
                TaxableAmount = g.Sum(d => d.TaxableAmount),
                CgstAmount    = g.Sum(d => d.CgstAmount),
                SgstAmount    = g.Sum(d => d.SgstAmount),
                IgstAmount    = g.Sum(d => d.IgstAmount),
                TotalTax      = g.Sum(d => d.CgstAmount + d.SgstAmount + d.IgstAmount + d.CessAmount),
                TotalAmount   = g.Sum(d => d.LineTotal)
            })
            .OrderBy(r => r.HsnCode).ThenBy(r => r.GstPercent)
            .ToListAsync(ct);

        var inward = await _uow.Repository<PurchaseDetail>().Query()
            .Where(d => d.Purchase!.Status == DocumentStatus.Posted
                        && d.Purchase.PurchaseDate >= range.FromDate.Date
                        && d.Purchase.PurchaseDate <= range.ToDate.Date)
            .GroupBy(d => new { d.HsnCode, d.GstPercent, d.Purchase!.IsInterState })
            .Select(g => new GstSummaryRowDto
            {
                HsnCode       = g.Key.HsnCode,
                GstPercent    = g.Key.GstPercent,
                IsInterState  = g.Key.IsInterState,
                DocumentCount = g.Select(d => d.PurchaseId).Distinct().LongCount(),
                TaxableAmount = g.Sum(d => d.TaxableAmount),
                CgstAmount    = g.Sum(d => d.CgstAmount),
                SgstAmount    = g.Sum(d => d.SgstAmount),
                IgstAmount    = g.Sum(d => d.IgstAmount),
                TotalTax      = g.Sum(d => d.CgstAmount + d.SgstAmount + d.IgstAmount + d.CessAmount),
                TotalAmount   = g.Sum(d => d.LineTotal)
            })
            .OrderBy(r => r.HsnCode).ThenBy(r => r.GstPercent)
            .ToListAsync(ct);

        return new GstReturnDto
        {
            Period          = range,
            OutwardSupplies = outward,
            InwardSupplies  = inward,
            // Output tax collected on sales, less input credit on purchases.
            // This is an indicative figure for the shop, not a filed return -
            // reverse charge, ineligible credit and adjustments are the CA's call.
            NetTaxPayable   = outward.Sum(o => o.TotalTax) - inward.Sum(i => i.TotalTax)
        };
    }
}
