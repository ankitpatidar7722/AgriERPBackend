using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Common.Models;
using AgriERP.Domain.Entities.Finance;
using AgriERP.Domain.Entities.Masters;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Masters;

/// <summary>
/// Reference data that fills dropdowns but is never edited from the UI - GST
/// slabs, HSN codes, states, storage locations. Grouped into one service and
/// one endpoint so a form loads its lookups in a single round trip instead of
/// four.
/// </summary>
public interface ILookupService
{
    Task<IReadOnlyList<LookupDto>> GetStatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GstSlabLookupDto>> GetGstSlabsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetHsnCodesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetStorageLocationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PaymentModeLookupDto>> GetPaymentModesAsync(CancellationToken ct = default);
    Task<FormLookupsDto> GetItemFormLookupsAsync(CancellationToken ct = default);
}

public class PaymentModeLookupDto : LookupDto
{
    /// <summary>Cheques and NEFT need a reference number; cash does not.</summary>
    public bool RequiresReference { get; set; }

    public bool IsBankMode { get; set; }
}

public class GstSlabLookupDto : LookupDto
{
    public decimal TotalRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
}

/// <summary>Everything the item form needs, in one response.</summary>
public class FormLookupsDto
{
    public IReadOnlyList<LookupDto> ItemSubGroups { get; set; } = Array.Empty<LookupDto>();
    public IReadOnlyList<LookupDto> Companies { get; set; } = Array.Empty<LookupDto>();
    public IReadOnlyList<LookupDto> Units { get; set; } = Array.Empty<LookupDto>();
    public IReadOnlyList<GstSlabLookupDto> GstSlabs { get; set; } = Array.Empty<GstSlabLookupDto>();
    public IReadOnlyList<LookupDto> HsnCodes { get; set; } = Array.Empty<LookupDto>();
    public IReadOnlyList<LookupDto> StorageLocations { get; set; } = Array.Empty<LookupDto>();
}

public class LookupService : ILookupService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IItemSubGroupService _itemSubGroups;
    private readonly ICompanyService _companies;
    private readonly IUnitService _units;

    public LookupService(
        IUnitOfWork uow,
        IMapper mapper,
        IItemSubGroupService itemSubGroups,
        ICompanyService companies,
        IUnitService units)
    {
        _uow = uow;
        _mapper = mapper;
        _itemSubGroups = itemSubGroups;
        _companies = companies;
        _units = units;
    }

    public async Task<IReadOnlyList<LookupDto>> GetStatesAsync(CancellationToken ct = default)
        => await _uow.Repository<State>().Query()
            .Where(s => s.IsActive)
            .OrderBy(s => s.StateName)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GstSlabLookupDto>> GetGstSlabsAsync(CancellationToken ct = default)
        => await _uow.Repository<GstSlab>().Query()
            .Where(g => g.IsActive)
            .OrderBy(g => g.TotalRate)
            .Select(g => new GstSlabLookupDto
            {
                Id        = g.GstSlabId,
                Code      = g.SlabName,
                Name      = g.SlabName,
                TotalRate = g.TotalRate,
                CgstRate  = g.CgstRate,
                SgstRate  = g.SgstRate,
                IgstRate  = g.IgstRate,
                IsActive  = g.IsActive
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LookupDto>> GetHsnCodesAsync(CancellationToken ct = default)
        => await _uow.Repository<HsnCode>().Query()
            .Where(h => h.IsActive)
            .OrderBy(h => h.Code)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LookupDto>> GetStorageLocationsAsync(CancellationToken ct = default)
        => await _uow.Repository<StorageLocation>().Query()
            .Where(l => l.IsActive && !l.IsDeleted)
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.LocationName)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PaymentModeLookupDto>> GetPaymentModesAsync(CancellationToken ct = default)
        => await _uow.Repository<PaymentMode>().Query()
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .Select(m => new PaymentModeLookupDto
            {
                Id                = m.PaymentModeId,
                Code              = m.ModeCode,
                Name              = m.ModeName,
                RequiresReference = m.RequiresReference,
                IsBankMode        = m.IsBankMode,
                IsActive          = m.IsActive
            })
            .ToListAsync(ct);

    public async Task<FormLookupsDto> GetItemFormLookupsAsync(CancellationToken ct = default)
    {
        // Sequential, not Task.WhenAll: they share one scoped DbContext, which
        // is not thread-safe. Parallelising here would throw
        // "A second operation was started on this context" under load.
        return new FormLookupsDto
        {
            ItemSubGroups       = await _itemSubGroups.GetLookupAsync(ct),
            Companies        = await _companies.GetLookupAsync(ct),
            Units            = await _units.GetLookupAsync(ct),
            GstSlabs         = await GetGstSlabsAsync(ct),
            HsnCodes         = await GetHsnCodesAsync(ct),
            StorageLocations = await GetStorageLocationsAsync(ct)
        };
    }
}
