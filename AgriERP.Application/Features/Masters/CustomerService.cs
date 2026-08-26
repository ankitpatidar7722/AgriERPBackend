using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Models;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Sales;
using AgriERP.Domain.Entities.Finance;
using AgriERP.Domain.Enums;
using AgriERP.Domain.ReadModels;
using AgriERP.Shared.Models;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Masters;

public interface ICustomerService
{
    Task<PagedResult<CustomerListDto>> GetPagedAsync(CustomerQueryParameters parameters, CancellationToken ct = default);
    Task<CustomerDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetLookupAsync(string? search, CancellationToken ct = default);
    Task<CustomerDto?> FindByMobileAsync(string mobile, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetVillagesAsync(CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default);
    Task<BulkImportResultDto> BulkImportAsync(IReadOnlyList<SaveCustomerRequest> rows, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(int id, SaveCustomerRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerOutstandingView>> GetOutstandingAsync(CancellationToken ct = default);
    Task<CustomerLedgerDto> GetLedgerAsync(int customerId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<CustomerProfileDto> GetProfileAsync(int customerId, CancellationToken ct = default);
    Task<ReceivablesSummaryDto> GetReceivablesSummaryAsync(CancellationToken ct = default);
}

public class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IDocumentNumberService _numbers;

    public CustomerService(IUnitOfWork uow, IMapper mapper, IDocumentNumberService numbers)
    {
        _uow = uow;
        _mapper = mapper;
        _numbers = numbers;
    }

    public async Task<PagedResult<CustomerListDto>> GetPagedAsync(
        CustomerQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Customer>().Query()
            .Where(c => !c.IsDeleted)
            .WhereIf(parameters.IsActive.HasValue, c => c.IsActive == parameters.IsActive!.Value)
            .WhereIf(!string.IsNullOrWhiteSpace(parameters.Village), c => c.Village == parameters.Village)
            .WhereIf(parameters.CustomerType.HasValue, c => c.CustomerType == parameters.CustomerType!.Value)
            // Name, mobile and village: the three things the counter actually
            // knows about a farmer standing in front of them.
            .WhereIf(search is not null, c =>
                c.CustomerName.Contains(search!) ||
                c.CustomerCode.Contains(search!) ||
                (c.Mobile != null && c.Mobile.Contains(search!)) ||
                (c.Village != null && c.Village.Contains(search!)));

        if (parameters.HasOutstanding == true)
        {
            var owing = _uow.Repository<Sale>().Query()
                .Where(s => s.Status == DocumentStatus.Posted && s.BalanceAmount > 0 && s.CustomerId != null)
                .Select(s => s.CustomerId!.Value);

            query = query.Where(c => owing.Contains(c.CustomerId));
        }

        query = parameters.SortBy?.Trim().ToLowerInvariant() switch
        {
            "code"    => query.OrderByDirection(c => c.CustomerCode, parameters.SortDescending),
            "village" => query.OrderByDirection(c => c.Village, parameters.SortDescending),
            "mobile"  => query.OrderByDirection(c => c.Mobile, parameters.SortDescending),
            "created" => query.OrderByDirection(c => c.CreatedAt, parameters.SortDescending),
            _         => query.OrderByDirection(c => c.CustomerName, parameters.SortDescending)
        };

        return await query.ToPagedResultAsync<Customer, CustomerListDto>(
            _mapper.ConfigurationProvider, parameters, ct);
    }

    public async Task<CustomerDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<Customer>().Query()
                .Where(c => c.CustomerId == id && !c.IsDeleted)
                .ProjectTo<CustomerDto>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Customer", id);

        await FillOutstandingAsync(dto, ct);
        return dto;
    }

    public async Task<CustomerDto?> FindByMobileAsync(string mobile, CancellationToken ct = default)
    {
        var normalized = mobile.Trim();

        var dto = await _uow.Repository<Customer>().Query()
            .Where(c => c.Mobile == normalized && !c.IsDeleted)
            .ProjectTo<CustomerDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(ct);

        if (dto is not null)
            await FillOutstandingAsync(dto, ct);

        return dto;
    }

    public async Task<IReadOnlyList<LookupDto>> GetLookupAsync(string? search, CancellationToken ct = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        return await _uow.Repository<Customer>().Query()
            .Where(c => !c.IsDeleted && c.IsActive)
            .WhereIf(term is not null, c =>
                c.CustomerName.Contains(term!) ||
                (c.Mobile != null && c.Mobile.Contains(term!)) ||
                (c.Village != null && c.Village.Contains(term!)))
            .OrderBy(c => c.CustomerName)
            // Capped: the billing screen's type-ahead must stay fast even when
            // the shop has ten thousand farmers on file.
            .Take(50)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetVillagesAsync(CancellationToken ct = default)
        => await _uow.Repository<Customer>().Query()
            .Where(c => !c.IsDeleted && c.Village != null && c.Village != "")
            .Select(c => c.Village!)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CustomerOutstandingView>> GetOutstandingAsync(CancellationToken ct = default)
        => await _uow.Repository<CustomerOutstandingView>().Query()
            .Where(v => v.OutstandingAmount != 0)
            .OrderByDescending(v => v.OutstandingAmount)
            .ToListAsync(ct);

    private const string VoucherSalesInvoice = "Sales Invoice";
    private static readonly string[] ReceiptVouchers = { "Payment Receipt", "Receipt" };

    public async Task<CustomerLedgerDto> GetLedgerAsync(
        int customerId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var customer = await _uow.Repository<Customer>().Query()
            .Where(c => c.CustomerId == customerId && !c.IsDeleted)
            .Select(c => new { c.CustomerId, c.CustomerCode, c.CustomerName })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Customer", customerId);

        // The whole ledger for this customer (few rows for a village shop). The
        // view already carries a correct cumulative RunningBalance per row.
        var all = await _uow.Repository<CustomerLedgerView>().Query()
            .Where(l => l.CustomerId == customerId)
            .OrderBy(l => l.Seq)
            .Select(l => new CustomerLedgerRowDto
            {
                Seq = l.Seq,
                TransactionDate = l.TransactionDate,
                VoucherType = l.VoucherType,
                VoucherNumber = l.VoucherNumber,
                ReferenceType = l.ReferenceType,
                ReferenceId = l.ReferenceId,
                Narration = l.Narration,
                Debit = l.Debit,
                Credit = l.Credit,
                RunningBalance = l.RunningBalance,
                CreatedByName = l.CreatedByName
            })
            .ToListAsync(ct);

        var fromDate = from?.Date;
        var toDate = to?.Date;

        var opening = fromDate.HasValue
            ? all.Where(r => r.TransactionDate < fromDate.Value).Sum(r => r.Debit - r.Credit)
            : 0m;

        var rows = all
            .Where(r => (!fromDate.HasValue || r.TransactionDate >= fromDate.Value)
                     && (!toDate.HasValue || r.TransactionDate <= toDate.Value))
            .ToList();

        return new CustomerLedgerDto
        {
            CustomerId = customer.CustomerId,
            CustomerCode = customer.CustomerCode,
            CustomerName = customer.CustomerName,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = opening,
            TotalDebit = rows.Sum(r => r.Debit),
            TotalCredit = rows.Sum(r => r.Credit),
            ClosingBalance = opening + rows.Sum(r => r.Debit - r.Credit),
            Rows = rows
        };
    }

    public async Task<ReceivablesSummaryDto> GetReceivablesSummaryAsync(CancellationToken ct = default)
    {
        var today = DateTime.Today;

        var totalOutstanding = await _uow.Repository<CustomerOutstandingView>().Query()
            .Where(v => v.OutstandingAmount > 0)
            .SumAsync(v => v.OutstandingAmount, ct);

        var overdue = _uow.Repository<Sale>().Query()
            .Where(s => s.CustomerId != null && s.Status == DocumentStatus.Posted
                        && s.BalanceAmount > 0 && s.DueDate != null && s.DueDate < today);

        var overdueCustomers = await overdue.Select(s => s.CustomerId).Distinct().CountAsync(ct);
        var overdueAmount = await overdue.SumAsync(s => s.BalanceAmount, ct);

        var nearDue = await _uow.Repository<Sale>().Query()
            .Where(s => s.CustomerId != null && s.Status == DocumentStatus.Posted
                        && s.BalanceAmount > 0 && s.DueDate != null
                        && s.DueDate >= today && s.DueDate <= today.AddDays(7))
            .Select(s => s.SaleId).CountAsync(ct);

        var todayCollection = await _uow.Repository<Payment>().Query()
            .Where(p => p.PartyType == PartyType.Customer && p.PaymentType == PaymentDirection.Receipt
                        && p.Status == PaymentRecordStatus.Posted && p.PaymentDate == today)
            .SumAsync(p => p.Amount, ct);

        return new ReceivablesSummaryDto
        {
            TotalOutstanding = totalOutstanding,
            OverdueCustomerCount = overdueCustomers,
            OverdueAmount = overdueAmount,
            NearDueCount = nearDue,
            TodayCollection = todayCollection
        };
    }

    public async Task<CustomerProfileDto> GetProfileAsync(int customerId, CancellationToken ct = default)
    {
        var c = await _uow.Repository<Customer>()
            .FirstOrDefaultAsync(x => x.CustomerId == customerId && !x.IsDeleted, tracking: false, ct)
            ?? throw new NotFoundException("Customer", customerId);

        var rows = await _uow.Repository<CustomerLedgerView>().Query()
            .Where(l => l.CustomerId == customerId)
            .Select(l => new { l.VoucherType, l.TransactionDate, l.Debit, l.Credit })
            .ToListAsync(ct);

        var outstanding = rows.Sum(r => r.Debit - r.Credit);
        var totalPurchases = rows.Where(r => r.VoucherType == VoucherSalesInvoice).Sum(r => r.Debit);
        var totalPayments = rows.Where(r => ReceiptVouchers.Contains(r.VoucherType)).Sum(r => r.Credit);
        var lastPurchase = rows.Where(r => r.VoucherType == VoucherSalesInvoice)
            .Select(r => (DateTime?)r.TransactionDate).DefaultIfEmpty(null).Max();
        var lastPayment = rows.Where(r => ReceiptVouchers.Contains(r.VoucherType))
            .Select(r => (DateTime?)r.TransactionDate).DefaultIfEmpty(null).Max();

        // Nearest due date among still-unpaid invoices.
        var nextDue = await _uow.Repository<Sale>().Query()
            .Where(s => s.CustomerId == customerId && s.Status == DocumentStatus.Posted
                        && s.BalanceAmount > 0 && s.DueDate != null)
            .OrderBy(s => s.DueDate)
            .Select(s => s.DueDate)
            .FirstOrDefaultAsync(ct);

        var signedOpening = c.OpeningBalanceType == BalanceType.DR ? c.OpeningBalance : -c.OpeningBalance;

        return new CustomerProfileDto
        {
            CustomerId = c.CustomerId,
            CustomerCode = c.CustomerCode,
            CustomerName = c.CustomerName,
            Mobile = c.Mobile,
            Village = c.Village,
            CustomerType = c.CustomerType.ToString(),
            IsActive = c.IsActive,
            CreditLimit = c.CreditLimit,
            CreditDays = c.CreditDays,
            OpeningBalance = signedOpening,
            Outstanding = outstanding,
            TotalPurchases = totalPurchases,
            TotalPayments = totalPayments,
            LastPurchaseDate = lastPurchase,
            LastPaymentDate = lastPayment,
            NextDueDate = nextDue,
            OverCreditLimit = c.CreditLimit > 0 && outstanding > c.CreditLimit
        };
    }

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default)
    {
        await GuardDuplicatesAsync(request, customerId: null, ct);

        var customer = _mapper.Map<Customer>(request);
        Normalize(customer, request);

        customer.CustomerCode = string.IsNullOrWhiteSpace(request.CustomerCode)
            ? await _numbers.NextAsync(DocumentType.Customer, ct)
            : request.CustomerCode.Trim().ToUpperInvariant();

        await _uow.Repository<Customer>().AddAsync(customer, ct);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(customer.CustomerId, ct);
    }

    /// <summary>
    /// Imports many customers (from an Excel upload). Each row goes through the
    /// same CreateAsync as a manual add, so it lands in the same table with the
    /// same code-series, validation and duplicate checks. Rows are independent:
    /// valid ones are saved and the rest reported with a reason (partial import).
    /// </summary>
    public async Task<BulkImportResultDto> BulkImportAsync(
        IReadOnlyList<SaveCustomerRequest> rows, CancellationToken ct = default)
    {
        var result = new BulkImportResultDto();
        for (var i = 0; i < rows.Count; i++)
        {
            try
            {
                await CreateAsync(rows[i], ct);
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add(new BulkImportError
                {
                    Row = i + 1,
                    Message = ex.InnerException?.Message ?? ex.Message,
                });
            }
            finally
            {
                // A failed SaveChanges leaves the row's entity tracked as Added;
                // clear it so it does not re-fail every subsequent row.
                _uow.ClearTracking();
            }
        }
        return result;
    }

    public async Task<CustomerDto> UpdateAsync(int id, SaveCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await _uow.Repository<Customer>()
            .FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Customer", id);

        await GuardDuplicatesAsync(request, id, ct);

        var existingCode = customer.CustomerCode;
        _mapper.Map(request, customer);
        Normalize(customer, request);
        customer.CustomerCode = existingCode;   // allocated once, printed on old bills

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var customer = await _uow.Repository<Customer>()
            .FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Customer", id);

        var invoiceCount = await _uow.Repository<Sale>().CountAsync(s => s.CustomerId == id, ct);

        if (invoiceCount > 0)
        {
            var due = await _uow.Repository<CustomerOutstandingView>().Query()
                .Where(v => v.CustomerId == id)
                .Select(v => (decimal?)v.OutstandingAmount)
                .FirstOrDefaultAsync(ct) ?? 0m;

            var suffix = due > 0 ? $" and {due:N2} still outstanding" : string.Empty;
            throw new ConflictException(
                $"'{customer.CustomerName}' has {invoiceCount} invoice(s){suffix}. " +
                "Set the customer inactive instead of deleting.");
        }

        customer.IsDeleted = true;
        customer.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }

    private async Task FillOutstandingAsync(CustomerDto dto, CancellationToken ct)
    {
        var outstanding = await _uow.Repository<CustomerOutstandingView>().Query()
            .FirstOrDefaultAsync(v => v.CustomerId == dto.CustomerId, ct);

        if (outstanding is null) return;

        dto.OutstandingAmount = outstanding.OutstandingAmount;
        dto.LastInvoiceDate = outstanding.LastInvoiceDate;
        dto.OldestUnpaidAgeDays = outstanding.OldestUnpaidAgeDays;
    }

    private static void Normalize(Customer customer, SaveCustomerRequest request)
    {
        customer.CustomerName = request.CustomerName.Trim();
        customer.FatherName = Blank(request.FatherName);
        customer.Village = Blank(request.Village);
        // NULL rather than empty string: the filtered unique index on Mobile
        // ignores NULLs but would collide on a second blank.
        customer.Mobile = Blank(request.Mobile);
        customer.AlternateMobile = Blank(request.AlternateMobile);
        customer.GstNumber = Blank(request.GstNumber)?.ToUpperInvariant();
        customer.Pincode = Blank(request.Pincode);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task GuardDuplicatesAsync(SaveCustomerRequest request, int? customerId, CancellationToken ct)
    {
        var mobile = Blank(request.Mobile);

        if (mobile is not null && await _uow.Repository<Customer>().AnyAsync(
                c => !c.IsDeleted && c.Mobile == mobile && c.CustomerId != customerId, ct))
            throw new ValidationException(nameof(request.Mobile),
                $"Mobile number {mobile} already belongs to another customer.");

        if (!string.IsNullOrWhiteSpace(request.CustomerCode))
        {
            var code = request.CustomerCode.Trim().ToUpperInvariant();
            if (await _uow.Repository<Customer>().AnyAsync(
                    c => !c.IsDeleted && c.CustomerCode == code && c.CustomerId != customerId, ct))
                throw new ValidationException(nameof(request.CustomerCode), $"Code '{code}' is already in use.");
        }
    }
}
