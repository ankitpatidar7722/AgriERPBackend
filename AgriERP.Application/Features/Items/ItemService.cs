using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Extensions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Models;
using AgriERP.Application.Features.Items.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.Items;
using AgriERP.Domain.Enums;
using AgriERP.Domain.ReadModels;
using AgriERP.Shared.Models;
using AutoMapper;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Items;

public interface IItemService
{
    Task<PagedResult<ItemListDto>> GetPagedAsync(ItemQueryParameters parameters, CancellationToken ct = default);
    Task<ItemDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ItemLookupDto?> FindByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<ItemLookupDto>> SearchForBillingAsync(string? search, CancellationToken ct = default);
    Task<ItemDto> CreateAsync(SaveItemRequest request, CancellationToken ct = default);
    Task<ItemDto> UpdateAsync(int id, SaveItemRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class ItemService : IItemService
{
    /// <summary>
    /// Matches the Warning band in vw_BatchStock. Both must move together,
    /// or the near-expiry filter and the near-expiry report would disagree.
    /// </summary>
    private const int NearExpiryDays = 90;

    private const string GenericBatchNumber = "GEN";

    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IDocumentNumberService _numbers;
    private readonly IDateTimeProvider _clock;

    public ItemService(IUnitOfWork uow, IMapper mapper, IDocumentNumberService numbers, IDateTimeProvider clock)
    {
        _uow = uow;
        _mapper = mapper;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<PagedResult<ItemListDto>> GetPagedAsync(
        ItemQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;
        var nearExpiryCutoff = _clock.Today.AddDays(NearExpiryDays);

        // Joined to vw_ItemStock rather than summing batches inline.
        // The view already rolls up quantity, value and status in one pass; a
        // correlated Sum() per column would issue three subqueries per row.
        // The join is inner because the view emits exactly one row per
        // non-deleted item, stocked or not.
        var query =
            from p in _uow.Repository<Item>().Query().Where(p => !p.IsDeleted)
            join s in _uow.Repository<ItemStockView>().Query() on p.ItemId equals s.ItemId
            select new ItemWithStock { Item = p, Stock = s };

        query = query
            .WhereIf(parameters.IsActive.HasValue, x => x.Item.IsActive == parameters.IsActive!.Value)
            .WhereIf(parameters.ItemGroupId.HasValue, x => x.Item.ItemGroupId == parameters.ItemGroupId)
            .WhereIf(parameters.ItemSubGroupId.HasValue, x => x.Item.ItemSubGroupId == parameters.ItemSubGroupId)
            .WhereIf(parameters.CompanyId.HasValue, x => x.Item.CompanyId == parameters.CompanyId)
            .WhereIf(parameters.GstSlabId.HasValue, x => x.Item.GstSlabId == parameters.GstSlabId)
            .WhereIf(!string.IsNullOrWhiteSpace(parameters.Barcode),
                     x => x.Item.Barcode == parameters.Barcode!.Trim())
            .WhereIf(!string.IsNullOrWhiteSpace(parameters.RackNumber),
                     x => x.Item.RackNumber == parameters.RackNumber!.Trim())
            // Technical name is in the search set because farmers ask for
            // "imidacloprid" at least as often as they ask for "Confidor".
            .WhereIf(search is not null, x =>
                x.Item.ItemName.Contains(search!) ||
                x.Item.ItemCode.Contains(search!) ||
                (x.Item.ShortName != null && x.Item.ShortName.Contains(search!)) ||
                (x.Item.TechnicalName != null && x.Item.TechnicalName.Contains(search!)) ||
                (x.Item.Brand != null && x.Item.Brand.Contains(search!)) ||
                (x.Item.Barcode != null && x.Item.Barcode == search!));

        query = parameters.StockStatus?.Trim().ToLowerInvariant() switch
        {
            "outofstock" => query.Where(x => x.Stock.CurrentStock <= 0),
            "instock"    => query.Where(x => x.Stock.CurrentStock > 0),
            "lowstock"   => query.Where(x => x.Stock.StockStatus == "LowStock"),
            "overstock"  => query.Where(x => x.Stock.StockStatus == "OverStock"),
            _            => query
        };

        if (parameters.NearExpiryOnly == true)
        {
            var expiring = _uow.Repository<ItemBatch>().Query()
                .Where(b => b.CurrentQty > 0
                            && b.ExpiryDate != null
                            && b.ExpiryDate <= nearExpiryCutoff)
                .Select(b => b.ItemId);

            query = query.Where(x => expiring.Contains(x.Item.ItemId));
        }

        query = parameters.SortBy?.Trim().ToLowerInvariant() switch
        {
            "code"      => query.OrderByDirection(x => x.Item.ItemCode, parameters.SortDescending),
            "itemSubGroup"  => query.OrderByDirection(x => x.Stock.ItemSubGroupName, parameters.SortDescending),
            "company"   => query.OrderByDirection(x => x.Stock.CompanyName, parameters.SortDescending),
            "stock"     => query.OrderByDirection(x => x.Stock.CurrentStock, parameters.SortDescending),
            "rate"      => query.OrderByDirection(x => x.Item.SellingRate, parameters.SortDescending),
            "mrp"       => query.OrderByDirection(x => x.Item.Mrp, parameters.SortDescending),
            "expiry"    => query.OrderByDirection(x => x.Stock.NearestExpiryDate, parameters.SortDescending),
            "created"   => query.OrderByDirection(x => x.Item.CreatedAt, parameters.SortDescending),
            _           => query.OrderByDirection(x => x.Item.ItemName, parameters.SortDescending)
        };

        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<ItemListDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .Skip(parameters.Skip)
            .Take(parameters.PageSize)
            .Select(x => new ItemListDto
            {
                ItemId       = x.Item.ItemId,
                ItemCode     = x.Item.ItemCode,
                ItemName     = x.Item.ItemName,
                ShortName       = x.Item.ShortName,
                TechnicalName   = x.Item.TechnicalName,
                ItemSubGroupId      = x.Item.ItemSubGroupId,
                ItemSubGroupName    = x.Stock.ItemSubGroupName,
                ItemGroupName       = x.Item.ItemGroup!.ItemGroupName,
                CompanyId       = x.Item.CompanyId,
                CompanyName     = x.Stock.CompanyName,
                Brand           = x.Item.Brand,
                PackingSize     = x.Item.PackingSize,
                PackingUnitCode = x.Item.PackingUnit != null ? x.Item.PackingUnit.UnitCode : null,
                UnitId          = x.Item.UnitId,
                UnitCode        = x.Stock.UnitCode,
                HsnCode         = x.Item.Hsn != null ? x.Item.Hsn.Code : null,
                GstPercent      = x.Item.GstSlab!.TotalRate,
                PurchaseRate    = x.Item.PurchaseRate,
                SellingRate     = x.Item.SellingRate,
                WholesaleRate   = x.Item.WholesaleRate,
                DealerRate      = x.Item.DealerRate,
                MinSellingRate  = x.Item.MinSellingRate,
                Mrp             = x.Item.Mrp,
                MinStockLevel   = x.Item.MinStockLevel,
                RackNumber      = x.Item.RackNumber,
                Barcode         = x.Item.Barcode,
                ImagePath       = x.Item.ImagePath,
                IsActive        = x.Item.IsActive,
                CurrentStock    = x.Stock.CurrentStock,
                StockStatus     = x.Stock.StockStatus,
                NearestExpiryDate = x.Stock.NearestExpiryDate
            })
            .ToListAsync(ct);

        return PagedResult<ItemListDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    public async Task<ItemDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        // The three related values are projected as scalars rather than pulled
        // in with Include. Include cannot follow a projection to an anonymous
        // type, and loading three whole entities to read one column from each
        // would be wasteful anyway.
        var row = await (
            from p in _uow.Repository<Item>().Query().Where(p => p.ItemId == id && !p.IsDeleted)
            join s in _uow.Repository<ItemStockView>().Query() on p.ItemId equals s.ItemId
            select new
            {
                p,
                s,
                PackingUnitCode = p.PackingUnit != null ? p.PackingUnit.UnitCode : null,
                HsnCode         = p.Hsn != null ? p.Hsn.Code : null,
                GstPercent      = p.GstSlab != null ? p.GstSlab.TotalRate : 0m
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Item", id);

        // Keyed by field id, which is what the edit form sends back - matching
        // on the field NAME instead is what leaves the source system's values
        // orphaned the moment a field is renamed.
        var extraFields = await _uow.Repository<ItemMasterDetail>()
            .Query()
            .Where(d => d.ItemId == id)
            .ToDictionaryAsync(d => d.ItemGroupFieldId, d => d.FieldValue, ct);

        var dto = new ItemDto
        {
            ItemGroupId       = row.p.ItemGroupId,
            ExtraFields       = extraFields,
            ItemId            = row.p.ItemId,
            ItemCode          = row.p.ItemCode,
            ItemName          = row.p.ItemName,
            ShortName            = row.p.ShortName,
            TechnicalName        = row.p.TechnicalName,
            ItemSubGroupId           = row.p.ItemSubGroupId,
            ItemSubGroupName         = row.s.ItemSubGroupName,
            CompanyId            = row.p.CompanyId,
            CompanyName          = row.s.CompanyName,
            Brand                = row.p.Brand,
            PackingSize          = row.p.PackingSize,
            PackingUnitId        = row.p.PackingUnitId,
            PackingUnitCode      = row.PackingUnitCode,
            UnitId               = row.p.UnitId,
            UnitCode             = row.s.UnitCode,
            PurchaseUnitId       = row.p.PurchaseUnitId,
            StockUnitId          = row.p.StockUnitId,
            HsnId                = row.p.HsnId,
            HsnCode              = row.HsnCode,
            GstSlabId            = row.p.GstSlabId,
            GstPercent           = row.GstPercent,
            IsRateInclusiveOfTax = row.p.IsRateInclusiveOfTax,
            PurchaseRate         = row.p.PurchaseRate,
            SellingRate          = row.p.SellingRate,
            Mrp                  = row.p.Mrp,
            WholesaleRate        = row.p.WholesaleRate,
            DealerRate           = row.p.DealerRate,
            MinSellingRate       = row.p.MinSellingRate,
            MinStockLevel        = row.p.MinStockLevel,
            MaxStockLevel        = row.p.MaxStockLevel,
            ReorderLevel         = row.p.ReorderLevel,
            IsBatchTracked       = row.p.IsBatchTracked,
            IsExpiryTracked      = row.p.IsExpiryTracked,
            AllowNegativeStock   = row.p.AllowNegativeStock,
            DefaultLocationId    = row.p.DefaultLocationId,
            RackNumber           = row.p.RackNumber,
            Barcode              = row.p.Barcode,
            ImagePath            = row.p.ImagePath,
            Description          = row.p.Description,
            LicenceNumber        = row.p.LicenceNumber,
            IsActive             = row.p.IsActive,
            CurrentStock         = row.s.CurrentStock,
            StockValueAtCost     = row.s.StockValueAtCost,
            BatchCount           = (int)row.s.BatchCount,
            StockStatus          = row.s.StockStatus,
            NearestExpiryDate    = row.s.NearestExpiryDate,
            CreatedAt            = row.p.CreatedAt,
            UpdatedAt            = row.p.UpdatedAt
        };

        dto.Batches = await GetBatchesAsync(id, ct);
        return dto;
    }

    public async Task<ItemLookupDto?> FindByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var normalized = barcode.Trim();

        return await BillingLookupQuery()
            .Where(x => x.Item.Barcode == normalized)
            .Select(BillingProjection())
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ItemLookupDto>> SearchForBillingAsync(
        string? search, CancellationToken ct = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        return await BillingLookupQuery()
            .WhereIf(term is not null, x =>
                x.Item.ItemName.Contains(term!) ||
                x.Item.ItemCode.Contains(term!) ||
                (x.Item.ShortName != null && x.Item.ShortName.Contains(term!)) ||
                (x.Item.TechnicalName != null && x.Item.TechnicalName.Contains(term!)) ||
                (x.Item.Barcode != null && x.Item.Barcode == term!))
            .OrderBy(x => x.Item.ItemName)
            // Capped so the billing type-ahead stays instant on a counter PC.
            .Take(30)
            .Select(BillingProjection())
            .ToListAsync(ct);
    }

    public async Task<ItemDto> CreateAsync(SaveItemRequest request, CancellationToken ct = default)
    {
        await GuardReferencesAsync(request, ct);
        await GuardDuplicatesAsync(request, itemId: null, ct);
        GuardRates(request);

        var item = _mapper.Map<Item>(request);
        Normalize(item, request);

        item.ItemGroupId = await ResolveItemGroupIdAsync(request, ct);

        item.ItemCode = string.IsNullOrWhiteSpace(request.ItemCode)
            ? await NextItemCodeAsync(item.ItemGroupId, ct)
            : request.ItemCode.Trim().ToUpperInvariant();

        await _uow.Repository<Item>().AddAsync(item, ct);
        await _uow.SaveChangesAsync(ct);

        await SaveExtraFieldsAsync(item, request, ct);

        // A item that is not batch-tracked still needs one batch row, so
        // every stock path in the system is identical and the billing code
        // never branches on "is this batched?".
        if (!item.IsBatchTracked)
            await CreateGenericBatchAsync(item, ct);

        return await GetByIdAsync(item.ItemId, ct);
    }

    public async Task<ItemDto> UpdateAsync(int id, SaveItemRequest request, CancellationToken ct = default)
    {
        var item = await _uow.Repository<Item>()
            .FirstOrDefaultAsync(p => p.ItemId == id && !p.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Item", id);

        await GuardReferencesAsync(request, ct);
        await GuardDuplicatesAsync(request, id, ct);
        GuardRates(request);

        var hadBatchTracking = item.IsBatchTracked;
        var existingCode = item.ItemCode;

        // Turning batch tracking off once stock exists would leave those
        // batches stranded - visible in the ledger, unreachable from billing.
        if (hadBatchTracking && !request.IsBatchTracked)
        {
            var trackedBatches = await _uow.Repository<ItemBatch>()
                .CountAsync(b => b.ItemId == id && b.BatchNumber != GenericBatchNumber && b.CurrentQty != 0, ct);

            if (trackedBatches > 0)
                throw new BusinessRuleException(
                    $"This item has {trackedBatches} batch(es) holding stock. " +
                    "Clear that stock before switching batch tracking off.",
                    "BATCH_TRACKING_LOCKED");
        }

        _mapper.Map(request, item);
        Normalize(item, request);
        item.ItemCode = existingCode;   // allocated once, printed on old bills
        item.ItemGroupId = await ResolveItemGroupIdAsync(request, ct);

        await _uow.SaveChangesAsync(ct);

        await SaveExtraFieldsAsync(item, request, ct);

        if (!item.IsBatchTracked)
            await EnsureGenericBatchAsync(item, ct);

        return await GetByIdAsync(id, ct);
    }

    /* --------------------------- item groups ---------------------------- */

    /// <summary>
    /// An item belongs to the group its sub-group belongs to.
    ///
    /// The client may send the group explicitly - it knows which form it drew -
    /// but it is checked against the sub-group rather than trusted. The two
    /// disagreeing means the form and the classification have drifted apart,
    /// and the item would then be saved under a group whose fields were never
    /// filled in.
    /// </summary>
    private async Task<int> ResolveItemGroupIdAsync(SaveItemRequest request, CancellationToken ct)
    {
        var subGroup = await _uow.Repository<ItemSubGroup>()
            .FirstOrDefaultAsync(c => c.ItemSubGroupId == request.ItemSubGroupId && !c.IsDeleted, tracking: false, ct)
            ?? throw new ValidationException(nameof(request.ItemSubGroupId), "Select a valid sub group.");

        if (request.ItemGroupId != 0 && request.ItemGroupId != subGroup.ItemGroupId)
            throw new ValidationException(nameof(request.ItemGroupId),
                "The item group does not match the sub group. Reload the form and try again.");

        return subGroup.ItemGroupId;
    }

    /// <summary>
    /// Allocates from the group's own series: P-000001 for a pesticide,
    /// S-000001 for a seed. One shared counter would make the code say nothing
    /// about what the item is, which is the point of having groups at all.
    /// </summary>
    private async Task<string> NextItemCodeAsync(int itemGroupId, CancellationToken ct)
    {
        var group = await _uow.Repository<ItemGroup>()
            .FirstOrDefaultAsync(g => g.ItemGroupId == itemGroupId, tracking: false, ct)
            ?? throw new NotFoundException("Item group", itemGroupId);

        return await _numbers.NextAsync($"Item_{group.ItemGroupCode}", ct);
    }

    /// <summary>
    /// Writes the values for fields that have no column of their own.
    ///
    /// Only fields the group actually defines are accepted. Silently dropping
    /// an unknown id would hide a stale form; writing it would leave a value
    /// nothing can ever read back, since the item screen only asks for the
    /// fields its group declares.
    /// </summary>
    private async Task SaveExtraFieldsAsync(Item item, SaveItemRequest request, CancellationToken ct)
    {
        var defined = await _uow.Repository<ItemGroupField>()
            .Query()
            .Where(f => f.ItemGroupId == item.ItemGroupId && !f.IsStoredOnItem && f.IsActive)
            .Select(f => new { f.ItemGroupFieldId, f.FieldDisplayName, f.IsRequired })
            .ToListAsync(ct);

        var definedIds = defined.Select(f => f.ItemGroupFieldId).ToHashSet();

        var unknown = request.ExtraFields.Keys.Where(id => !definedIds.Contains(id)).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(nameof(request.ExtraFields),
                $"Field(s) {string.Join(", ", unknown)} do not belong to this item group.");

        foreach (var field in defined.Where(f => f.IsRequired))
        {
            request.ExtraFields.TryGetValue(field.ItemGroupFieldId, out var supplied);
            if (string.IsNullOrWhiteSpace(supplied))
                throw new ValidationException(nameof(request.ExtraFields), $"{field.FieldDisplayName} is required.");
        }

        var repository = _uow.Repository<ItemMasterDetail>();
        var existing = await repository
            .Query(tracking: true)
            .Where(d => d.ItemId == item.ItemId)
            .ToListAsync(ct);

        foreach (var (fieldId, value) in request.ExtraFields)
        {
            var row = existing.FirstOrDefault(d => d.ItemGroupFieldId == fieldId);
            if (row is null)
            {
                await repository.AddAsync(new ItemMasterDetail
                {
                    ItemId           = item.ItemId,
                    ItemGroupFieldId = fieldId,
                    FieldValue       = value,
                    CreatedAt        = _clock.UtcNow
                }, ct);
            }
            else if (row.FieldValue != value)
            {
                row.FieldValue = value;
                row.UpdatedAt = _clock.UtcNow;
            }
        }

        // A field the group no longer asks for must not keep answering. Left
        // behind, it would reappear the day the field is switched back on,
        // carrying a value nobody has looked at since.
        foreach (var orphan in existing.Where(d => !definedIds.Contains(d.ItemGroupFieldId)))
            repository.Remove(orphan);

        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var item = await _uow.Repository<Item>()
            .FirstOrDefaultAsync(p => p.ItemId == id && !p.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Item", id);

        var stockOnHand = await _uow.Repository<ItemBatch>().Query()
            .Where(b => b.ItemId == id)
            .SumAsync(b => (decimal?)b.CurrentQty, ct) ?? 0m;

        if (stockOnHand != 0)
            throw new ConflictException(
                $"'{item.ItemName}' still has {stockOnHand:N3} in stock. " +
                "Adjust the stock to zero first, or set the item inactive.");

        item.IsDeleted = true;
        item.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }

    // ---- helpers ------------------------------------------------------------

    private IQueryable<ItemWithStock> BillingLookupQuery()
        => from p in _uow.Repository<Item>().Query().Where(p => !p.IsDeleted && p.IsActive)
           join s in _uow.Repository<ItemStockView>().Query() on p.ItemId equals s.ItemId
           select new ItemWithStock { Item = p, Stock = s };

    private static System.Linq.Expressions.Expression<Func<ItemWithStock, ItemLookupDto>> BillingProjection()
        => x => new ItemLookupDto
        {
            Id             = x.Item.ItemId,
            Code           = x.Item.ItemCode,
            Name           = x.Item.ItemName,
            ShortName      = x.Item.ShortName,
            Description    = x.Item.TechnicalName,
            Barcode        = x.Item.Barcode,
            UnitId         = x.Item.UnitId,
            UnitCode       = x.Stock.UnitCode,
            SellingRate    = x.Item.SellingRate,
            WholesaleRate  = x.Item.WholesaleRate,
            DealerRate     = x.Item.DealerRate,
            Mrp            = x.Item.Mrp,
            MinSellingRate = x.Item.MinSellingRate,
            GstPercent     = x.Item.GstSlab!.TotalRate,
            HsnCode        = x.Item.Hsn != null ? x.Item.Hsn.Code : null,
            CurrentStock   = x.Stock.CurrentStock,
            IsActive       = x.Item.IsActive
        };

    private async Task<IReadOnlyList<ItemBatchDto>> GetBatchesAsync(int itemId, CancellationToken ct)
    {
        var batches = await _uow.Repository<ItemBatch>().Query()
            .Where(b => b.ItemId == itemId)
            .Include(b => b.Location)
            // FEFO order, matching what the billing screen will offer.
            .OrderBy(b => b.ExpiryDate == null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchId)
            .ToListAsync(ct);

        var today = _clock.Today;

        return batches.Select(b =>
        {
            var dto = _mapper.Map<ItemBatchDto>(b);
            dto.DaysToExpiry = b.ExpiryDate.HasValue
                ? (int)(b.ExpiryDate.Value.Date - today).TotalDays
                : null;
            dto.ExpiryStatus = b.ExpiryDate switch
            {
                null                                  => "NoExpiry",
                var e when e.Value.Date < today       => "Expired",
                var e when e.Value.Date <= today.AddDays(30) => "Critical",
                var e when e.Value.Date <= today.AddDays(NearExpiryDays) => "Warning",
                _                                     => "Safe"
            };
            return dto;
        }).ToList();
    }

    private async Task CreateGenericBatchAsync(Item item, CancellationToken ct)
    {
        var locationId = item.DefaultLocationId ?? await GetDefaultLocationIdAsync(ct);

        await _uow.Repository<ItemBatch>().AddAsync(new ItemBatch
        {
            ItemId    = item.ItemId,
            BatchNumber  = GenericBatchNumber,
            LocationId   = locationId,
            PurchaseRate = item.PurchaseRate,
            SellingRate  = item.SellingRate,
            Mrp          = item.Mrp
        }, ct);

        await _uow.SaveChangesAsync(ct);
    }

    private async Task EnsureGenericBatchAsync(Item item, CancellationToken ct)
    {
        var exists = await _uow.Repository<ItemBatch>()
            .AnyAsync(b => b.ItemId == item.ItemId && b.BatchNumber == GenericBatchNumber, ct);

        if (!exists)
            await CreateGenericBatchAsync(item, ct);
    }

    private async Task<int> GetDefaultLocationIdAsync(CancellationToken ct)
        => await _uow.Repository<StorageLocation>().Query()
               .Where(l => l.IsDefault && !l.IsDeleted)
               .Select(l => l.LocationId)
               .FirstOrDefaultAsync(ct)
           is var id && id != 0
               ? id
               : throw new BusinessRuleException(
                   "No default storage location is configured. Set one in Storage Locations first.",
                   "NO_DEFAULT_LOCATION");

    private static void Normalize(Item item, SaveItemRequest request)
    {
        item.ItemName = request.ItemName.Trim();
        item.ShortName = Blank(request.ShortName);
        item.TechnicalName = Blank(request.TechnicalName);
        item.Brand = Blank(request.Brand);
        // Barcode, rack and licence are no longer captured on the item form, so
        // they are left untouched here - an existing value survives an edit.
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void GuardRates(SaveItemRequest request)
    {
        // Selling below cost is legitimate (clearing near-expiry stock), so it
        // is not blocked here. Selling below the shop's OWN declared floor is
        // a configuration mistake and is.
        if (request.MinSellingRate > 0 && request.SellingRate < request.MinSellingRate)
            throw new ValidationException(nameof(request.SellingRate),
                "Selling rate cannot be below the minimum selling rate.");

        if (request.Mrp > 0 && request.SellingRate > request.Mrp)
            throw new ValidationException(nameof(request.SellingRate),
                "Selling rate cannot exceed the MRP - billing above MRP is not permitted.");

        if (request.MaxStockLevel > 0 && request.MaxStockLevel < request.MinStockLevel)
            throw new ValidationException(nameof(request.MaxStockLevel),
                "Maximum stock level cannot be below the minimum.");
    }

    private async Task GuardReferencesAsync(SaveItemRequest request, CancellationToken ct)
    {
        if (!await _uow.Repository<ItemSubGroup>().AnyAsync(
                c => c.ItemSubGroupId == request.ItemSubGroupId && !c.IsDeleted, ct))
            throw new ValidationException(nameof(request.ItemSubGroupId), "The selected itemSubGroup does not exist.");

        if (!await _uow.Repository<Unit>().AnyAsync(u => u.UnitId == request.UnitId && !u.IsDeleted, ct))
            throw new ValidationException(nameof(request.UnitId), "The selected unit does not exist.");

        if (request.PurchaseUnitId is { } purchaseUnitId &&
            !await _uow.Repository<Unit>().AnyAsync(u => u.UnitId == purchaseUnitId && !u.IsDeleted, ct))
            throw new ValidationException(nameof(request.PurchaseUnitId), "The selected purchase unit does not exist.");

        if (request.StockUnitId is { } stockUnitId &&
            !await _uow.Repository<Unit>().AnyAsync(u => u.UnitId == stockUnitId && !u.IsDeleted, ct))
            throw new ValidationException(nameof(request.StockUnitId), "The selected stock unit does not exist.");

        if (!await _uow.Repository<GstSlab>().AnyAsync(g => g.GstSlabId == request.GstSlabId, ct))
            throw new ValidationException(nameof(request.GstSlabId), "The selected GST slab does not exist.");

        if (request.CompanyId is { } companyId &&
            !await _uow.Repository<Company>().AnyAsync(c => c.CompanyId == companyId && !c.IsDeleted, ct))
            throw new ValidationException(nameof(request.CompanyId), "The selected company does not exist.");

        // A pack size without its unit renders as a bare number on the shelf
        // label, so both or neither.
        if (request.PackingSize.HasValue && request.PackingUnitId is null)
            throw new ValidationException(nameof(request.PackingUnitId),
                "Select a packing unit when a packing size is entered.");
    }

    private async Task GuardDuplicatesAsync(SaveItemRequest request, int? itemId, CancellationToken ct)
    {
        var name = request.ItemName.Trim();

        // Mirrors UQ_Items_Name_Company_Packing. The same brand in two pack
        // sizes is two items; the same brand and pack entered twice is a
        // duplicate that would split the stock figure in half.
        if (await _uow.Repository<Item>().AnyAsync(
                p => !p.IsDeleted
                     && p.ItemName == name
                     && p.CompanyId == request.CompanyId
                     && p.PackingSize == request.PackingSize
                     && p.PackingUnitId == request.PackingUnitId
                     && p.ItemId != itemId, ct))
            throw new ValidationException(nameof(request.ItemName),
                $"'{name}' already exists for this company and pack size.");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
        {
            var code = request.ItemCode.Trim().ToUpperInvariant();
            if (await _uow.Repository<Item>().AnyAsync(
                    p => !p.IsDeleted && p.ItemCode == code && p.ItemId != itemId, ct))
                throw new ValidationException(nameof(request.ItemCode), $"Code '{code}' is already in use.");
        }
    }

    /// <summary>Join carrier for Item + its rolled-up stock row.</summary>
    private class ItemWithStock
    {
        public Item Item { get; set; } = null!;
        public ItemStockView Stock { get; set; } = null!;
    }
}
