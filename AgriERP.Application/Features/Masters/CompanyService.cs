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

public interface ICompanyService
{
    Task<PagedResult<CompanyListDto>> GetPagedAsync(CompanyQueryParameters parameters, CancellationToken ct = default);
    Task<CompanyDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default);
    Task<CompanyDto> CreateAsync(SaveCompanyRequest request, CancellationToken ct = default);
    Task<CompanyDto> UpdateAsync(int id, SaveCompanyRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class CompanyService : ICompanyService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public CompanyService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<PagedResult<CompanyListDto>> GetPagedAsync(
        CompanyQueryParameters parameters, CancellationToken ct = default)
    {
        var search = parameters.NormalizedSearch;

        var query = _uow.Repository<Company>().Query()
            .Where(c => !c.IsDeleted)
            .WhereIf(parameters.IsActive.HasValue, c => c.IsActive == parameters.IsActive!.Value)
            .WhereIf(parameters.StateId.HasValue, c => c.StateId == parameters.StateId)
            .WhereIf(search is not null, c =>
                c.CompanyName.Contains(search!) ||
                c.CompanyCode.Contains(search!) ||
                (c.GstNumber != null && c.GstNumber.Contains(search!)) ||
                (c.Phone != null && c.Phone.Contains(search!)) ||
                (c.ContactPerson != null && c.ContactPerson.Contains(search!)));

        query = sortKey(parameters.SortBy) switch
        {
            "code"    => query.OrderByDirection(c => c.CompanyCode, parameters.SortDescending),
            "city"    => query.OrderByDirection(c => c.City, parameters.SortDescending),
            "created" => query.OrderByDirection(c => c.CreatedAt, parameters.SortDescending),
            _         => query.OrderByDirection(c => c.CompanyName, parameters.SortDescending)
        };

        return await query.ToPagedResultAsync<Company, CompanyListDto>(
            _mapper.ConfigurationProvider, parameters, ct);

        static string sortKey(string? s) => s?.Trim().ToLowerInvariant() ?? "name";
    }

    public async Task<CompanyDto> GetByIdAsync(int id, CancellationToken ct = default)
        => await _uow.Repository<Company>().Query()
               .Where(c => c.CompanyId == id && !c.IsDeleted)
               .ProjectTo<CompanyDto>(_mapper.ConfigurationProvider)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Company", id);

    public async Task<IReadOnlyList<LookupDto>> GetLookupAsync(CancellationToken ct = default)
        => await _uow.Repository<Company>().Query()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.CompanyName)
            .ProjectTo<LookupDto>(_mapper.ConfigurationProvider)
            .ToListAsync(ct);

    public async Task<CompanyDto> CreateAsync(SaveCompanyRequest request, CancellationToken ct = default)
    {
        await GuardDuplicatesAsync(request, companyId: null, ct);

        var company = _mapper.Map<Company>(request);
        Normalize(company, request);

        await _uow.Repository<Company>().AddAsync(company, ct);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(company.CompanyId, ct);
    }

    public async Task<CompanyDto> UpdateAsync(int id, SaveCompanyRequest request, CancellationToken ct = default)
    {
        var company = await _uow.Repository<Company>()
            .FirstOrDefaultAsync(c => c.CompanyId == id && !c.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Company", id);

        await GuardDuplicatesAsync(request, id, ct);

        _mapper.Map(request, company);
        Normalize(company, request);

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var company = await _uow.Repository<Company>()
            .FirstOrDefaultAsync(c => c.CompanyId == id && !c.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("Company", id);

        var itemCount = await _uow.Repository<Item>()
            .CountAsync(p => p.CompanyId == id && !p.IsDeleted, ct);

        if (itemCount > 0)
            throw new ConflictException(
                $"'{company.CompanyName}' is the manufacturer of {itemCount} item(s). " +
                "Set it inactive instead of deleting.");

        company.IsDeleted = true;
        company.IsActive = false;
        await _uow.SaveChangesAsync(ct);
    }

    private static void Normalize(Company company, SaveCompanyRequest request)
    {
        company.CompanyCode = request.CompanyCode.Trim().ToUpperInvariant();
        company.CompanyName = request.CompanyName.Trim();
        // Blank strings become NULL: the filtered unique index on GstNumber
        // ignores NULLs but would collide on a second empty string.
        company.GstNumber = Blank(request.GstNumber)?.ToUpperInvariant();
        company.Email = Blank(request.Email);
        company.Pincode = Blank(request.Pincode);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task GuardDuplicatesAsync(SaveCompanyRequest request, int? companyId, CancellationToken ct)
    {
        var code = request.CompanyCode.Trim().ToUpperInvariant();
        var name = request.CompanyName.Trim();
        var gst = Blank(request.GstNumber)?.ToUpperInvariant();

        if (await _uow.Repository<Company>().AnyAsync(
                c => !c.IsDeleted && c.CompanyCode == code && c.CompanyId != companyId, ct))
            throw new ValidationException(nameof(request.CompanyCode), $"Code '{code}' is already in use.");

        if (await _uow.Repository<Company>().AnyAsync(
                c => !c.IsDeleted && c.CompanyName == name && c.CompanyId != companyId, ct))
            throw new ValidationException(nameof(request.CompanyName), $"'{name}' already exists.");

        if (gst is not null && await _uow.Repository<Company>().AnyAsync(
                c => !c.IsDeleted && c.GstNumber == gst && c.CompanyId != companyId, ct))
            throw new ValidationException(nameof(request.GstNumber),
                $"GST number '{gst}' is already registered to another company.");
    }
}
