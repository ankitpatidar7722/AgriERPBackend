using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Models;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Masters;

public interface IWarehouseService
{
    Task<PagedResult<WarehouseListDto>> GetPagedAsync(WarehouseQueryParameters parameters, CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default);
    Task<WarehouseDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<string?> PeekNextCodeAsync(CancellationToken ct = default);
    Task<WarehouseDto> CreateAsync(SaveWarehouseRequest request, CancellationToken ct = default);
    Task<WarehouseDto> UpdateAsync(int id, SaveWarehouseRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class WarehouseService : IWarehouseService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IDocumentNumberService _numbers;

    public WarehouseService(IUnitOfWork uow, IMapper mapper, IDocumentNumberService numbers)
    {
        _uow = uow;
        _mapper = mapper;
        _numbers = numbers;
    }

    public async Task<PagedResult<WarehouseListDto>> GetPagedAsync(
        WarehouseQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<WarehouseMaster>().Query()
            .Where(w => !w.IsDeleted)
            .WhereIf(parameters.IsActive.HasValue, w => w.IsActive == parameters.IsActive!.Value)
            .WhereIf(search is not null, w =>
                w.WarehouseName.Contains(search!) ||
                w.WarehouseCode.Contains(search!) ||
                (w.Address != null && w.Address.Contains(search!)));

        query = sortKey(parameters.SortBy) switch
        {
            "code"    => query.OrderByDirection(w => w.WarehouseCode, parameters.SortDescending),
            "created" => query.OrderByDirection(w => w.CreatedAt, parameters.SortDescending),
            _         => query.OrderByDirection(w => w.WarehouseName, parameters.SortDescending)
        };

        return await query.ToPagedResultAsync<WarehouseMaster, WarehouseListDto>(
            _mapper.ConfigurationProvider, parameters, ct);

        static string sortKey(string? s) => s?.Trim().ToLowerInvariant() ?? "name";
    }

    public async Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default)
        => await _uow.Repository<WarehouseMaster>().Query()
            .Where(w => !w.IsDeleted && w.IsActive)
            .OrderBy(w => w.WarehouseName)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<WarehouseDto> GetByIdAsync(int id, CancellationToken ct = default)
        => await _uow.Repository<WarehouseMaster>().Query()
               .Where(w => w.WarehouseId == id && !w.IsDeleted)
               .ProjectTo<WarehouseDto>(_mapper.ConfigurationProvider)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Warehouse", id);

    /// <summary>The code the next warehouse would get, for the create form. Does not consume the series.</summary>
    public Task<string?> PeekNextCodeAsync(CancellationToken ct = default)
        => _numbers.PeekNextAsync(DocumentType.Warehouse, ct);

    public async Task<WarehouseDto> CreateAsync(SaveWarehouseRequest request, CancellationToken ct = default)
    {
        var warehouseId = await _uow.ExecuteInTransactionAsync(async token =>
        {
            var warehouse = new WarehouseMaster
            {
                // Auto code inside the transaction: a failed save releases the number.
                WarehouseCode = await _numbers.NextAsync(DocumentType.Warehouse, token),
                WarehouseName = request.WarehouseName.Trim(),
                Address = Blank(request.Address),
                IsActive = request.IsActive,
                Bins = CleanBins(request.Bins).Select(b => new WarehouseBin { BinName = b }).ToList()
            };

            await _uow.Repository<WarehouseMaster>().AddAsync(warehouse, token);
            await _uow.SaveChangesAsync(token);
            return warehouse.WarehouseId;
        }, ct);

        return await GetByIdAsync(warehouseId, ct);
    }

    public async Task<WarehouseDto> UpdateAsync(int id, SaveWarehouseRequest request, CancellationToken ct = default)
    {
        var warehouse = await _uow.Repository<WarehouseMaster>().Query(tracking: true)
            .Include(w => w.Bins)
            .FirstOrDefaultAsync(w => w.WarehouseId == id && !w.IsDeleted, ct)
            ?? throw new NotFoundException("Warehouse", id);

        warehouse.WarehouseName = request.WarehouseName.Trim();
        warehouse.Address = Blank(request.Address);
        warehouse.IsActive = request.IsActive;

        // Bins are replaced wholesale. Clearing a required-relationship child
        // collection makes EF delete the orphans; the fresh names are re-added.
        warehouse.Bins.Clear();
        foreach (var name in CleanBins(request.Bins))
            warehouse.Bins.Add(new WarehouseBin { BinName = name });

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var warehouse = await _uow.Repository<WarehouseMaster>()
            .FirstOrDefaultAsync(w => w.WarehouseId == id && !w.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Warehouse", id);

        // Standalone master: nothing references it, so a delete is a soft delete.
        // Bins stay attached to the hidden row and reappear if it is ever restored.
        warehouse.IsDeleted = true;
        warehouse.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trim, drop blanks, de-duplicate (case-insensitive), keep order.</summary>
    private static IEnumerable<string> CleanBins(IEnumerable<string>? bins)
    {
        if (bins is null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bin in bins)
        {
            var trimmed = bin?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (seen.Add(trimmed)) yield return trimmed;
        }
    }
}
