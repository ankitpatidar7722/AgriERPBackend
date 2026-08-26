using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Models;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Purchases;
using AgriERP.Domain.Enums;
using AgriERP.Domain.ReadModels;
using AgriERP.Shared.Models;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Masters;

public interface ISupplierService
{
    Task<PagedResult<SupplierListDto>> GetPagedAsync(SupplierQueryParameters parameters, CancellationToken ct = default);
    Task<SupplierDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default);
    Task<SupplierDto> CreateAsync(SaveSupplierRequest request, CancellationToken ct = default);
    Task<BulkImportResultDto> BulkImportAsync(IReadOnlyList<SaveSupplierRequest> rows, CancellationToken ct = default);
    Task<SupplierDto> UpdateAsync(int id, SaveSupplierRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierOutstandingView>> GetOutstandingAsync(CancellationToken ct = default);
    Task<SupplierLedgerDto> GetLedgerAsync(int supplierId, DateTime? from, DateTime? to, CancellationToken ct = default);
}

public class SupplierService : ISupplierService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IDocumentNumberService _numbers;

    public SupplierService(IUnitOfWork uow, IMapper mapper, IDocumentNumberService numbers)
    {
        _uow = uow;
        _mapper = mapper;
        _numbers = numbers;
    }

    public async Task<PagedResult<SupplierListDto>> GetPagedAsync(
        SupplierQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Supplier>().Query()
            .Where(s => !s.IsDeleted)
            .WhereIf(parameters.IsActive.HasValue, s => s.IsActive == parameters.IsActive!.Value)
            .WhereIf(parameters.StateId.HasValue, s => s.StateId == parameters.StateId)
            .WhereIf(!string.IsNullOrWhiteSpace(parameters.City), s => s.City == parameters.City)
            .WhereIf(search is not null, s =>
                s.SupplierName.Contains(search!) ||
                s.SupplierCode.Contains(search!) ||
                (s.Phone != null && s.Phone.Contains(search!)) ||
                (s.GstNumber != null && s.GstNumber.Contains(search!)) ||
                (s.ContactPerson != null && s.ContactPerson.Contains(search!)));

        if (parameters.HasOutstanding == true)
        {
            // Correlated against posted bills only. A draft purchase is not
            // money owed - it has not touched stock or the ledger yet.
            var owing = _uow.Repository<Purchase>().Query()
                .Where(p => p.Status == DocumentStatus.Posted && p.BalanceAmount > 0)
                .Select(p => p.SupplierId);

            query = query.Where(s => owing.Contains(s.SupplierId));
        }

        query = parameters.SortBy?.Trim().ToLowerInvariant() switch
        {
            "code"    => query.OrderByDirection(s => s.SupplierCode, parameters.SortDescending),
            "city"    => query.OrderByDirection(s => s.City, parameters.SortDescending),
            "created" => query.OrderByDirection(s => s.CreatedAt, parameters.SortDescending),
            _         => query.OrderByDirection(s => s.SupplierName, parameters.SortDescending)
        };

        return await query.ToPagedResultAsync<Supplier, SupplierListDto>(
            _mapper.ConfigurationProvider, parameters, ct);
    }

    public async Task<SupplierDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _uow.Repository<Supplier>().Query()
                .Where(s => s.SupplierId == id && !s.IsDeleted)
                .ProjectTo<SupplierDto>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Supplier", id);

        // Outstanding is derived, so it comes from the view rather than a
        // column that would drift the first time a bill was edited.
        var outstanding = await _uow.Repository<SupplierOutstandingView>().Query()
            .FirstOrDefaultAsync(v => v.SupplierId == id, ct);

        dto.OutstandingAmount = outstanding?.OutstandingAmount ?? 0m;
        return dto;
    }

    public async Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default)
        => await _uow.Repository<Supplier>().Query()
            .Where(s => !s.IsDeleted && s.IsActive)
            .OrderBy(s => s.SupplierName)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SupplierOutstandingView>> GetOutstandingAsync(CancellationToken ct = default)
        => await _uow.Repository<SupplierOutstandingView>().Query()
            .Where(v => v.OutstandingAmount != 0)
            .OrderByDescending(v => v.OutstandingAmount)
            .ToListAsync(ct);

    public async Task<SupplierLedgerDto> GetLedgerAsync(
        int supplierId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var supplier = await _uow.Repository<Supplier>().Query()
            .Where(s => s.SupplierId == supplierId && !s.IsDeleted)
            .Select(s => new { s.SupplierId, s.SupplierCode, s.SupplierName })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Supplier", supplierId);

        // The whole ledger for this supplier (few rows for a shop). The view
        // already carries a correct cumulative RunningBalance per row.
        var all = await _uow.Repository<SupplierLedgerView>().Query()
            .Where(l => l.SupplierId == supplierId)
            .OrderBy(l => l.Seq)
            .Select(l => new SupplierLedgerRowDto
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

        return new SupplierLedgerDto
        {
            SupplierId = supplier.SupplierId,
            SupplierCode = supplier.SupplierCode,
            SupplierName = supplier.SupplierName,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = opening,
            TotalDebit = rows.Sum(r => r.Debit),
            TotalCredit = rows.Sum(r => r.Credit),
            ClosingBalance = opening + rows.Sum(r => r.Debit - r.Credit),
            Rows = rows
        };
    }

    public async Task<SupplierDto> CreateAsync(SaveSupplierRequest request, CancellationToken ct = default)
    {
        await GuardDuplicatesAsync(request, supplierId: null, ct);

        var supplier = _mapper.Map<Supplier>(request);
        Normalize(supplier, request);

        supplier.SupplierCode = string.IsNullOrWhiteSpace(request.SupplierCode)
            ? await _numbers.NextAsync(DocumentType.Supplier, ct)
            : request.SupplierCode.Trim().ToUpperInvariant();

        await _uow.Repository<Supplier>().AddAsync(supplier, ct);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(supplier.SupplierId, ct);
    }

    /// <summary>
    /// Imports many suppliers (from an Excel upload). Each row reuses CreateAsync
    /// so it lands in the same table with the same code-series, validation and
    /// duplicate checks. Rows are independent: valid ones save, the rest are
    /// reported with a reason (partial import).
    /// </summary>
    public async Task<BulkImportResultDto> BulkImportAsync(
        IReadOnlyList<SaveSupplierRequest> rows, CancellationToken ct = default)
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
                _uow.ClearTracking();
            }
        }
        return result;
    }

    public async Task<SupplierDto> UpdateAsync(int id, SaveSupplierRequest request, CancellationToken ct = default)
    {
        var supplier = await _uow.Repository<Supplier>()
            .FirstOrDefaultAsync(s => s.SupplierId == id && !s.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Supplier", id);

        await GuardDuplicatesAsync(request, id, ct);

        var existingCode = supplier.SupplierCode;
        _mapper.Map(request, supplier);
        Normalize(supplier, request);

        // The code is allocated once and never reissued. Purchase bills printed
        // last season carry it, and changing it orphans that paper trail.
        supplier.SupplierCode = existingCode;

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var supplier = await _uow.Repository<Supplier>()
            .FirstOrDefaultAsync(s => s.SupplierId == id && !s.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Supplier", id);

        var billCount = await _uow.Repository<Purchase>()
            .CountAsync(p => p.SupplierId == id, ct);

        if (billCount > 0)
            throw new ConflictException(
                $"'{supplier.SupplierName}' has {billCount} purchase bill(s) on record. " +
                "Set the supplier inactive instead - deleting would break those bills.");

        supplier.IsDeleted = true;
        supplier.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }

    private static void Normalize(Supplier supplier, SaveSupplierRequest request)
    {
        supplier.SupplierName = request.SupplierName.Trim();
        supplier.GstNumber = Blank(request.GstNumber)?.ToUpperInvariant();
        supplier.PanNumber = Blank(request.PanNumber)?.ToUpperInvariant();
        supplier.BankIfsc = Blank(request.BankIfsc)?.ToUpperInvariant();
        supplier.Email = Blank(request.Email);
        supplier.Pincode = Blank(request.Pincode);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task GuardDuplicatesAsync(SaveSupplierRequest request, int? supplierId, CancellationToken ct)
    {
        var gst = Blank(request.GstNumber)?.ToUpperInvariant();

        if (gst is not null && await _uow.Repository<Supplier>().AnyAsync(
                s => !s.IsDeleted && s.GstNumber == gst && s.SupplierId != supplierId, ct))
            throw new ValidationException(nameof(request.GstNumber),
                $"GST number '{gst}' is already registered to another supplier.");

        if (!string.IsNullOrWhiteSpace(request.SupplierCode))
        {
            var code = request.SupplierCode.Trim().ToUpperInvariant();
            if (await _uow.Repository<Supplier>().AnyAsync(
                    s => !s.IsDeleted && s.SupplierCode == code && s.SupplierId != supplierId, ct))
                throw new ValidationException(nameof(request.SupplierCode), $"Code '{code}' is already in use.");
        }
    }
}
