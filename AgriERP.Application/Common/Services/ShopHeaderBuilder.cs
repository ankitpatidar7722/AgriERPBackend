using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Sales.Dtos;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Entities.System;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Common.Services;

/// <summary>
/// Builds a printed-document letterhead from ShopMaster - the shop the user
/// manages on the Shop &amp; Warehouse screen - falling back to CompanyProfile for
/// the fields ShopMaster does not keep (pincode, licences, UPI, terms/footer,
/// logo) so the head is complete either way. Single-shop setup: the first
/// active ShopMaster row is "the shop". Also returns the shop's state id, for
/// inter-state GST detection.
///
/// Shared by every print that carries the shop head (purchase order, sales
/// order) so the ShopMaster-first rule lives in exactly one place.
/// </summary>
public static class ShopHeaderBuilder
{
    public static async Task<(ShopHeaderDto Header, int? StateId)> BuildAsync(
        IUnitOfWork uow, CancellationToken ct = default)
    {
        var master = await uow.Repository<ShopMaster>().Query()
            .Where(s => !s.IsDeleted && s.IsActive)
            .OrderBy(s => s.ShopId)
            .Select(s => new
            {
                s.ShopName,
                s.Address,
                s.City,
                StateName = s.State != null ? s.State.StateName : null,
                s.StateId,
                s.GstNo,
                s.MobileNo,
                s.Email
            })
            .FirstOrDefaultAsync(ct);

        var company = await uow.Repository<CompanyProfile>().Query()
            .Select(c => new
            {
                c.ShopName,
                c.GstNumber,
                c.AddressLine1,
                c.City,
                StateName = c.State != null ? c.State.StateName : null,
                c.StateId,
                c.Pincode,
                c.Phone,
                c.Email,
                c.PesticideLicenceNo,
                c.SeedLicenceNo,
                c.FertilizerLicenceNo,
                c.InvoiceTerms,
                c.InvoiceFooterNote,
                c.UpiId,
                c.LogoPath
            })
            .FirstOrDefaultAsync(ct);

        static string? Pick(string? primary, string? fallback)
            => string.IsNullOrWhiteSpace(primary) ? fallback : primary;

        var header = new ShopHeaderDto
        {
            ShopName            = Pick(master?.ShopName, company?.ShopName) ?? "My Agriculture Shop",
            GstNumber           = Pick(master?.GstNo, company?.GstNumber),
            Address             = Pick(master?.Address, company?.AddressLine1),
            City                = Pick(master?.City, company?.City),
            StateName           = Pick(master?.StateName, company?.StateName),
            Pincode             = company?.Pincode,               // ShopMaster keeps none
            Phone               = Pick(master?.MobileNo, company?.Phone),
            Email               = Pick(master?.Email, company?.Email),
            PesticideLicenceNo  = company?.PesticideLicenceNo,
            SeedLicenceNo       = company?.SeedLicenceNo,
            FertilizerLicenceNo = company?.FertilizerLicenceNo,
            InvoiceTerms        = company?.InvoiceTerms,
            InvoiceFooterNote   = company?.InvoiceFooterNote,
            UpiId               = company?.UpiId,
            LogoPath            = company?.LogoPath
        };

        return (header, master?.StateId ?? company?.StateId);
    }
}
