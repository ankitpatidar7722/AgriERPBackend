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

public interface IItemSubGroupService
{
    Task<PagedResult<ItemSubGroupListDto>> GetPagedAsync(ItemSubGroupQueryParameters parameters, CancellationToken ct = default);
    Task<ItemSubGroupDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default);
    Task<ItemSubGroupDto> CreateAsync(SaveItemSubGroupRequest request, CancellationToken ct = default);
    Task<ItemSubGroupDto> UpdateAsync(int id, SaveItemSubGroupRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class ItemSubGroupService : IItemSubGroupService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public ItemSubGroupService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<PagedResult<ItemSubGroupListDto>> GetPagedAsync(
        ItemSubGroupQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<ItemSubGroup>().Query()
            .Where(c => !c.IsDeleted)
            .WhereIf(parameters.IsActive.HasValue, c => c.IsActive == parameters.IsActive!.Value)
            .WhereIf(parameters.ParentItemSubGroupId.HasValue, c => c.ParentItemSubGroupId == parameters.ParentItemSubGroupId)
            .WhereIf(parameters.RootOnly == true, c => c.ParentItemSubGroupId == null)
            .WhereIf(search is not null, c =>
                c.ItemSubGroupName.Contains(search!) ||
                c.ItemSubGroupCode.Contains(search!) ||
                (c.Description != null && c.Description.Contains(search!)));

        query = ApplySort(query, parameters.SortBy, parameters.SortDescending);

        return await query.ToPagedResultAsync<ItemSubGroup, ItemSubGroupListDto>(
            _mapper.ConfigurationProvider, parameters, ct);
    }

    public async Task<ItemSubGroupDto> GetByIdAsync(int id, CancellationToken ct = default)
        => await _uow.Repository<ItemSubGroup>().Query()
               .Where(c => c.ItemSubGroupId == id && !c.IsDeleted)
               .ProjectTo<ItemSubGroupDto>(_mapper.ConfigurationProvider)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("ItemSubGroup", id);

    public async Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default)
        => await _uow.Repository<ItemSubGroup>().Query()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.ItemSubGroupName)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<ItemSubGroupDto> CreateAsync(SaveItemSubGroupRequest request, CancellationToken ct = default)
    {
        await GuardDuplicatesAsync(request, itemSubGroupId: null, ct);
        await GuardParentAsync(request.ParentItemSubGroupId, itemSubGroupId: null, ct);

        var itemSubGroup = _mapper.Map<ItemSubGroup>(request);
        itemSubGroup.ItemGroupId = await ResolveItemGroupIdAsync(request, ct);
        itemSubGroup.ItemSubGroupCode = request.ItemSubGroupCode.Trim().ToUpperInvariant();
        itemSubGroup.ItemSubGroupName = request.ItemSubGroupName.Trim();

        await _uow.Repository<ItemSubGroup>().AddAsync(itemSubGroup, ct);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(itemSubGroup.ItemSubGroupId, ct);
    }

    public async Task<ItemSubGroupDto> UpdateAsync(int id, SaveItemSubGroupRequest request, CancellationToken ct = default)
    {
        var itemSubGroup = await _uow.Repository<ItemSubGroup>()
            .FirstOrDefaultAsync(c => c.ItemSubGroupId == id && !c.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("ItemSubGroup", id);

        await GuardDuplicatesAsync(request, id, ct);
        await GuardParentAsync(request.ParentItemSubGroupId, id, ct);

        var newGroupId = await ResolveItemGroupIdAsync(request, ct);

        // Moving a sub-group between groups would leave the items under it on
        // a form that no longer matches: a seed suddenly asked for a CIB
        // licence number, with its germination value stranded in
        // ItemMasterDetails under a field its new group does not define.
        if (newGroupId != itemSubGroup.ItemGroupId)
        {
            var affected = await _uow.Repository<Item>()
                .CountAsync(p => p.ItemSubGroupId == id && !p.IsDeleted, ct);

            if (affected > 0)
                throw new BusinessRuleException(
                    $"This sub group holds {affected} item(s), so it cannot be moved to another item group. " +
                    "Move the items first.",
                    "SUBGROUP_GROUP_LOCKED");
        }

        _mapper.Map(request, itemSubGroup);
        itemSubGroup.ItemGroupId = newGroupId;
        itemSubGroup.ItemSubGroupCode = request.ItemSubGroupCode.Trim().ToUpperInvariant();
        itemSubGroup.ItemSubGroupName = request.ItemSubGroupName.Trim();

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    /// <summary>
    /// A sub-group with a parent inherits the parent's group; one without must
    /// name it. Inheriting rather than asking is what keeps "Vegetable Seeds"
    /// from ending up under Fertilizers Master while "Seeds" stays under Seed
    /// Master - a split that would only show up as a wrong entry form later.
    /// </summary>
    private async Task<int> ResolveItemGroupIdAsync(SaveItemSubGroupRequest request, CancellationToken ct)
    {
        if (request.ParentItemSubGroupId is int parentId)
        {
            var parent = await _uow.Repository<ItemSubGroup>()
                .FirstOrDefaultAsync(c => c.ItemSubGroupId == parentId && !c.IsDeleted, tracking: false, ct)
                ?? throw new ValidationException(nameof(request.ParentItemSubGroupId), "Select a valid parent.");

            if (request.ItemGroupId != 0 && request.ItemGroupId != parent.ItemGroupId)
                throw new ValidationException(nameof(request.ItemGroupId),
                    "A sub group must sit in the same item group as its parent.");

            return parent.ItemGroupId;
        }

        if (request.ItemGroupId == 0)
            throw new ValidationException(nameof(request.ItemGroupId), "Select an item group.");

        var exists = await _uow.Repository<ItemGroup>()
            .CountAsync(g => g.ItemGroupId == request.ItemGroupId && !g.IsDeleted, ct);

        if (exists == 0)
            throw new ValidationException(nameof(request.ItemGroupId), "Select a valid item group.");

        return request.ItemGroupId;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var itemSubGroup = await _uow.Repository<ItemSubGroup>()
            .FirstOrDefaultAsync(c => c.ItemSubGroupId == id && !c.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("ItemSubGroup", id);

        // Blocked rather than cascaded. Soft-deleting a itemSubGroup silently
        // strips the classification from every item under it, and stock
        // reports grouped by itemSubGroup would quietly stop adding up.
        var itemCount = await _uow.Repository<Item>()
            .CountAsync(p => p.ItemSubGroupId == id && !p.IsDeleted, ct);

        if (itemCount > 0)
            throw new ConflictException(
                $"'{itemSubGroup.ItemSubGroupName}' is used by {itemCount} item(s). " +
                "Reassign or remove them first, or set the itemSubGroup inactive instead.");

        var childCount = await _uow.Repository<ItemSubGroup>()
            .CountAsync(c => c.ParentItemSubGroupId == id && !c.IsDeleted, ct);

        if (childCount > 0)
            throw new ConflictException(
                $"'{itemSubGroup.ItemSubGroupName}' has {childCount} sub-categorie(s). Remove them first.");

        itemSubGroup.IsDeleted = true;
        itemSubGroup.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }

    // ---- helpers ------------------------------------------------------------

    private static IQueryable<ItemSubGroup> ApplySort(IQueryable<ItemSubGroup> query, string? sortBy, bool descending)
        // A whitelist, not a dynamic property lookup: an unknown key falls back
        // to the default rather than throwing, so a stale bookmark from an
        // older UI build still returns data.
        => sortBy?.Trim().ToLowerInvariant() switch
        {
            "code"    => query.OrderByDirection(c => c.ItemSubGroupCode, descending),
            "name"    => query.OrderByDirection(c => c.ItemSubGroupName, descending),
            "created" => query.OrderByDirection(c => c.CreatedAt, descending),
            "active"  => query.OrderByDirection(c => c.IsActive, descending),
            // Roots first, then display order. Parents and children number
            // their display order independently, so ordering by it alone
            // interleaves "Vegetable Seeds" between "Insecticide" and
            // "Pesticide" - which reads as a broken list rather than a tree.
            _         => query.OrderBy(c => c.ParentItemSubGroupId != null)
                              .ThenBy(c => c.DisplayOrder)
                              .ThenBy(c => c.ItemSubGroupName)
        };

    private async Task GuardDuplicatesAsync(SaveItemSubGroupRequest request, int? itemSubGroupId, CancellationToken ct)
    {
        var code = request.ItemSubGroupCode.Trim().ToUpperInvariant();
        var name = request.ItemSubGroupName.Trim();

        // Checked here so the user gets a field-level message instead of a raw
        // unique-index violation. The database index remains the real guarantee
        // under concurrency.
        if (await _uow.Repository<ItemSubGroup>().AnyAsync(
                c => !c.IsDeleted && c.ItemSubGroupCode == code && c.ItemSubGroupId != itemSubGroupId, ct))
            throw new ValidationException(nameof(request.ItemSubGroupCode), $"Code '{code}' is already in use.");

        if (await _uow.Repository<ItemSubGroup>().AnyAsync(
                c => !c.IsDeleted && c.ItemSubGroupName == name && c.ItemSubGroupId != itemSubGroupId, ct))
            throw new ValidationException(nameof(request.ItemSubGroupName), $"'{name}' already exists.");
    }

    private async Task GuardParentAsync(int? parentId, int? itemSubGroupId, CancellationToken ct)
    {
        if (parentId is null) return;

        if (parentId == itemSubGroupId)
            throw new ValidationException(nameof(SaveItemSubGroupRequest.ParentItemSubGroupId),
                "A itemSubGroup cannot be its own parent.");

        var parent = await _uow.Repository<ItemSubGroup>()
            .FirstOrDefaultAsync(c => c.ItemSubGroupId == parentId && !c.IsDeleted, tracking: false, ct)
            ?? throw new ValidationException(nameof(SaveItemSubGroupRequest.ParentItemSubGroupId),
                "The selected parent itemSubGroup does not exist.");

        // Two levels is all the UI renders, and it is all an agri shop needs:
        // Seeds -> Vegetable Seeds. Deeper nesting would also need cycle
        // detection on every save.
        if (parent.ParentItemSubGroupId is not null)
            throw new ValidationException(nameof(SaveItemSubGroupRequest.ParentItemSubGroupId),
                $"'{parent.ItemSubGroupName}' is already a sub-itemSubGroup. ItemSubGroups nest one level only.");
    }
}
