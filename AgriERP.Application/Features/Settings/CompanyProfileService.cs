using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Settings.Dtos;
using AgriERP.Domain.Entities.System;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Settings;

public interface ICompanyProfileService
{
    Task<CompanyProfileDto> GetAsync(CancellationToken ct = default);
    Task<CompanyProfileDto> UpdateAsync(SaveCompanyProfileRequest request, CancellationToken ct = default);
}

/// <summary>
/// Reads/writes the single CompanyProfile row (the shop's invoice-header
/// identity: UPI id, dealer licences, bank, terms). The row is a seeded
/// singleton pinned to id 1; Update upserts it so a fresh install still works.
/// </summary>
public class CompanyProfileService : ICompanyProfileService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public CompanyProfileService(IUnitOfWork uow, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _uow = uow;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CompanyProfileDto> GetAsync(CancellationToken ct = default)
        => await _uow.Repository<CompanyProfile>().Query()
               .Select(c => new CompanyProfileDto
               {
                   ShopName = c.ShopName,
                   LegalName = c.LegalName,
                   GstNumber = c.GstNumber,
                   PanNumber = c.PanNumber,
                   PesticideLicenceNo = c.PesticideLicenceNo,
                   SeedLicenceNo = c.SeedLicenceNo,
                   FertilizerLicenceNo = c.FertilizerLicenceNo,
                   AddressLine1 = c.AddressLine1,
                   AddressLine2 = c.AddressLine2,
                   City = c.City,
                   StateId = c.StateId,
                   StateName = c.State != null ? c.State.StateName : null,
                   Pincode = c.Pincode,
                   Phone = c.Phone,
                   AlternatePhone = c.AlternatePhone,
                   Email = c.Email,
                   Website = c.Website,
                   BankName = c.BankName,
                   BankAccountNumber = c.BankAccountNumber,
                   BankIfsc = c.BankIfsc,
                   BankBranch = c.BankBranch,
                   UpiId = c.UpiId,
                   InvoiceTerms = c.InvoiceTerms,
                   InvoiceFooterNote = c.InvoiceFooterNote,
                   DataGovInKeySet = c.DataGovInApiKey != null && c.DataGovInApiKey != "",
                   UpdatedAt = c.UpdatedAt
               })
               .FirstOrDefaultAsync(ct)
           ?? new CompanyProfileDto();

    public async Task<CompanyProfileDto> UpdateAsync(SaveCompanyProfileRequest request, CancellationToken ct = default)
    {
        var profile = await _uow.Repository<CompanyProfile>()
            .FirstOrDefaultAsync(c => c.CompanyProfileId == 1, tracking: true, ct);

        var isNew = profile is null;
        profile ??= new CompanyProfile { CompanyProfileId = 1 };

        profile.ShopName            = request.ShopName.Trim();
        profile.LegalName           = Blank(request.LegalName);
        profile.GstNumber           = Blank(request.GstNumber)?.ToUpperInvariant();
        profile.PanNumber           = Blank(request.PanNumber)?.ToUpperInvariant();
        profile.PesticideLicenceNo  = Blank(request.PesticideLicenceNo);
        profile.SeedLicenceNo       = Blank(request.SeedLicenceNo);
        profile.FertilizerLicenceNo = Blank(request.FertilizerLicenceNo);
        profile.AddressLine1        = Blank(request.AddressLine1);
        profile.AddressLine2        = Blank(request.AddressLine2);
        profile.City                = Blank(request.City);
        profile.StateId             = request.StateId;
        profile.Pincode             = Blank(request.Pincode);
        profile.Phone               = Blank(request.Phone);
        profile.AlternatePhone      = Blank(request.AlternatePhone);
        profile.Email               = Blank(request.Email);
        profile.Website             = Blank(request.Website);
        profile.BankName            = Blank(request.BankName);
        profile.BankAccountNumber   = Blank(request.BankAccountNumber);
        profile.BankIfsc            = Blank(request.BankIfsc)?.ToUpperInvariant();
        profile.BankBranch          = Blank(request.BankBranch);
        profile.UpiId               = Blank(request.UpiId);
        profile.InvoiceTerms        = Blank(request.InvoiceTerms);
        profile.InvoiceFooterNote   = Blank(request.InvoiceFooterNote);
        // Blank key = keep the existing one (it is never sent back to the client,
        // so a plain settings save must not wipe it).
        if (!string.IsNullOrWhiteSpace(request.DataGovInApiKey))
            profile.DataGovInApiKey = request.DataGovInApiKey.Trim();
        profile.UpdatedAt           = _clock.UtcNow;
        profile.UpdatedBy           = _currentUser.UserId;

        if (isNew)
            await _uow.Repository<CompanyProfile>().AddAsync(profile, ct);

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
