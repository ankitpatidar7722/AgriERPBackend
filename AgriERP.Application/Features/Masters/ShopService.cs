using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Shared.Models;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Masters;

public interface IShopService
{
    Task<PagedResult<ShopListDto>> GetPagedAsync(ShopQueryParameters parameters, CancellationToken ct = default);
    Task<ShopListDto?> GetCurrentAsync(CancellationToken ct = default);
    Task<ShopDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ShopDto> CreateAsync(SaveShopRequest request, CancellationToken ct = default);
    Task<ShopDto> UpdateAsync(int id, SaveShopRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class ShopService : IShopService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public ShopService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<PagedResult<ShopListDto>> GetPagedAsync(
        ShopQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<ShopMaster>().Query()
            .Where(s => !s.IsDeleted)
            .WhereIf(parameters.IsActive.HasValue, s => s.IsActive == parameters.IsActive!.Value)
            .WhereIf(parameters.StateId.HasValue, s => s.StateId == parameters.StateId)
            .WhereIf(search is not null, s =>
                s.ShopName.Contains(search!) ||
                (s.OwnerName != null && s.OwnerName.Contains(search!)) ||
                (s.City != null && s.City.Contains(search!)) ||
                (s.GstNo != null && s.GstNo.Contains(search!)) ||
                (s.MobileNo != null && s.MobileNo.Contains(search!)));

        query = sortKey(parameters.SortBy) switch
        {
            "city"    => query.OrderByDirection(s => s.City, parameters.SortDescending),
            "owner"   => query.OrderByDirection(s => s.OwnerName, parameters.SortDescending),
            "created" => query.OrderByDirection(s => s.CreatedAt, parameters.SortDescending),
            _         => query.OrderByDirection(s => s.ShopName, parameters.SortDescending)
        };

        return await query.ToPagedResultAsync<ShopMaster, ShopListDto>(
            _mapper.ConfigurationProvider, parameters, ct);

        static string sortKey(string? s) => s?.Trim().ToLowerInvariant() ?? "name";
    }

    /// <summary>The shop the app runs as: the first active shop. Drives the header identity.</summary>
    public async Task<ShopListDto?> GetCurrentAsync(CancellationToken ct = default)
        => await _uow.Repository<ShopMaster>().Query()
               .Where(s => !s.IsDeleted && s.IsActive)
               .OrderBy(s => s.ShopId)
               .ProjectTo<ShopListDto>(_mapper.ConfigurationProvider)
               .FirstOrDefaultAsync(ct);

    public async Task<ShopDto> GetByIdAsync(int id, CancellationToken ct = default)
        => await _uow.Repository<ShopMaster>().Query()
               .Where(s => s.ShopId == id && !s.IsDeleted)
               .ProjectTo<ShopDto>(_mapper.ConfigurationProvider)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Shop", id);

    public async Task<ShopDto> CreateAsync(SaveShopRequest request, CancellationToken ct = default)
    {
        await GuardDuplicatesAsync(request, shopId: null, ct);

        var shop = _mapper.Map<ShopMaster>(request);
        Normalize(shop, request);

        await _uow.Repository<ShopMaster>().AddAsync(shop, ct);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(shop.ShopId, ct);
    }

    public async Task<ShopDto> UpdateAsync(int id, SaveShopRequest request, CancellationToken ct = default)
    {
        var shop = await _uow.Repository<ShopMaster>()
            .FirstOrDefaultAsync(s => s.ShopId == id && !s.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Shop", id);

        await GuardDuplicatesAsync(request, id, ct);

        _mapper.Map(request, shop);
        Normalize(shop, request);

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var shop = await _uow.Repository<ShopMaster>()
            .FirstOrDefaultAsync(s => s.ShopId == id && !s.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Shop", id);

        // Nothing references a shop, so a delete is a plain soft delete.
        shop.IsDeleted = true;
        shop.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }

    private static void Normalize(ShopMaster shop, SaveShopRequest request)
    {
        shop.ShopName = request.ShopName.Trim();
        shop.GstNo = Blank(request.GstNo)?.ToUpperInvariant();
        shop.OwnerName = Blank(request.OwnerName);
        shop.MobileNo = Blank(request.MobileNo);
        shop.Email = Blank(request.Email);
        shop.City = Blank(request.City);
        shop.Address = Blank(request.Address);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task GuardDuplicatesAsync(SaveShopRequest request, int? shopId, CancellationToken ct)
    {
        var name = request.ShopName.Trim();

        if (await _uow.Repository<ShopMaster>().AnyAsync(
                s => !s.IsDeleted && s.ShopName == name && s.ShopId != shopId, ct))
            throw new ValidationException(nameof(request.ShopName), $"A shop named '{name}' already exists.");
    }
}
