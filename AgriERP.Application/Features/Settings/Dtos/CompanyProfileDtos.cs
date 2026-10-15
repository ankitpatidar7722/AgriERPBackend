namespace AgriERP.Application.Features.Settings.Dtos;

/// <summary>
/// The single CompanyProfile row - the shop's own identity used on invoice/
/// document letterheads (UPI, dealer licences, bank, terms). One row only
/// (CK_CompanyProfile_Singleton pins the id to 1).
/// </summary>
public class CompanyProfileDto
{
    public string ShopName { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? GstNumber { get; set; }
    public string? PanNumber { get; set; }

    // Dealer licences printed on the invoice / compliance registers.
    public string? PesticideLicenceNo { get; set; }
    public string? SeedLicenceNo { get; set; }
    public string? FertilizerLicenceNo { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? StateName { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }

    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
    public string? UpiId { get; set; }

    public string? InvoiceTerms { get; set; }
    public string? InvoiceFooterNote { get; set; }

    /// <summary>Whether a data.gov.in mandi key is saved. The key itself is never
    /// returned to the client - only this flag, so the form can show "set / not set".</summary>
    public bool DataGovInKeySet { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public class SaveCompanyProfileRequest
{
    public string ShopName { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? GstNumber { get; set; }
    public string? PanNumber { get; set; }

    public string? PesticideLicenceNo { get; set; }
    public string? SeedLicenceNo { get; set; }
    public string? FertilizerLicenceNo { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }

    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
    public string? UpiId { get; set; }

    public string? InvoiceTerms { get; set; }
    public string? InvoiceFooterNote { get; set; }

    /// <summary>New data.gov.in mandi key. Blank/null = keep the existing key
    /// (so saving other fields never wipes it, and the key is never echoed back).</summary>
    public string? DataGovInApiKey { get; set; }
}
