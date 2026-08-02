using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Models;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Shared.Models;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Masters;

public interface IUnitService
{
    Task<PagedResult<UnitDto>> GetPagedAsync(UnitQueryParameters parameters, CancellationToken ct = default);
    Task<UnitDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default);
    Task<UnitDto> CreateAsync(SaveUnitRequest request, CancellationToken ct = default);
    Task<UnitDto> UpdateAsync(int id, SaveUnitRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class UnitService : IUnitService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public UnitService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<PagedResult<UnitDto>> GetPagedAsync(
        UnitQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Unit>().Query()
            .Where(u => !u.IsDeleted)
            .WhereIf(parameters.IsActive.HasValue, u => u.IsActive == parameters.IsActive!.Value)
            .WhereIf(search is not null, u =>
                u.UnitCode.Contains(search!) || u.UnitName.Contains(search!));

        query = parameters.SortBy?.Trim().ToLowerInvariant() switch
        {
            "code" => query.OrderByDirection(u => u.UnitCode, parameters.SortDescending),
            "name" => query.OrderByDirection(u => u.UnitName, parameters.SortDescending),
            _      => query.OrderByDirection(u => u.DisplayOrder, parameters.SortDescending)
                           .ThenBy(u => u.UnitCode)
        };

        return await query.ToPagedResultAsync<Unit, UnitDto>(
            _mapper.ConfigurationProvider, parameters, ct);
    }

    public async Task<UnitDto> GetByIdAsync(int id, CancellationToken ct = default)
        => await _uow.Repository<Unit>().Query()
               .Where(u => u.UnitId == id && !u.IsDeleted)
               .ProjectTo<UnitDto>(_mapper.ConfigurationProvider)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Unit", id);

    public async Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default)
        => await _uow.Repository<Unit>().Query()
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.DisplayOrder).ThenBy(u => u.UnitCode)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<UnitDto> CreateAsync(SaveUnitRequest request, CancellationToken ct = default)
    {
        var code = request.UnitCode.Trim().ToUpperInvariant();

        if (await _uow.Repository<Unit>().AnyAsync(u => !u.IsDeleted && u.UnitCode == code, ct))
            throw new ValidationException(nameof(request.UnitCode), $"Unit '{code}' already exists.");

        var unit = _mapper.Map<Unit>(request);
        unit.UnitCode = code;
        unit.UnitName = request.UnitName.Trim();

        await _uow.Repository<Unit>().AddAsync(unit, ct);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(unit.UnitId, ct);
    }

    public async Task<UnitDto> UpdateAsync(int id, SaveUnitRequest request, CancellationToken ct = default)
    {
        var unit = await _uow.Repository<Unit>()
            .FirstOrDefaultAsync(u => u.UnitId == id && !u.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Unit", id);

        var code = request.UnitCode.Trim().ToUpperInvariant();

        if (await _uow.Repository<Unit>().AnyAsync(
                u => !u.IsDeleted && u.UnitCode == code && u.UnitId != id, ct))
            throw new ValidationException(nameof(request.UnitCode), $"Unit '{code}' already exists.");

        _mapper.Map(request, unit);
        unit.UnitCode = code;
        unit.UnitName = request.UnitName.Trim();

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var unit = await _uow.Repository<Unit>()
            .FirstOrDefaultAsync(u => u.UnitId == id && !u.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Unit", id);

        // Checks BOTH roles a unit plays. A unit used only as a packing unit
        // ("250 ML") is just as load-bearing as the selling unit, and missing
        // that case would leave items rendering a blank pack size.
        var usedCount = await _uow.Repository<Item>()
            .CountAsync(p => !p.IsDeleted && (p.UnitId == id || p.PackingUnitId == id), ct);

        if (usedCount > 0)
            throw new ConflictException(
                $"Unit '{unit.UnitCode}' is used by {usedCount} item(s). Set it inactive instead.");

        unit.IsDeleted = true;
        unit.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }
}
