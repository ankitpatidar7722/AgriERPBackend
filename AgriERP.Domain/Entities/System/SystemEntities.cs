using AgriERP.Domain.Common;
using AgriERP.Domain.Entities.Masters;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.System;

/// <summary>
/// Maps to CompanyProfile - the shop itself, not a manufacturer.
///
/// Exactly one row exists: CK_CompanyProfile_Singleton pins the primary key to
/// 1. A second row would make "which GST number is ours?" ambiguous on every
/// invoice, and StateId here is what decides CGST+SGST versus IGST.
/// </summary>
public class CompanyProfile : IHasRowVersion
{
    public int CompanyProfileId { get; set; } = 1;
    public string ShopName { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? GstNumber { get; set; }
    public string? PanNumber { get; set; }

    // Dealer licences printed on the invoice per state agriculture department rules.
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

    public string? LogoPath { get; set; }
    public string? InvoiceTerms { get; set; }
    public string? InvoiceFooterNote { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[]? RowVersion { get; set; }

    public State? State { get; set; }
}

/// <summary>Maps to FinancialYears. Indian FY runs 1 April to 31 March.</summary>
public class FinancialYear
{
    public int FinancialYearId { get; set; }
    public string YearCode { get; set; } = string.Empty;    // '2026-27'
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>Exactly one year is active; a filtered unique index enforces it.</summary>
    public bool IsActive { get; set; }

    /// <summary>Once closed, no document may be posted into this year.</summary>
    public bool IsClosed { get; set; }

    public DateTime? ClosedAt { get; set; }
    public int? ClosedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<NumberSeries> NumberSeries { get; set; } = new List<NumberSeries>();
}

/// <summary>
/// Maps to NumberSeries.
///
/// Invoice numbers cannot have gaps and cannot repeat - a GST auditor will ask.
/// Never read CurrentNumber and increment it in C#: two salesmen pressing Save
/// at the same instant would both read the same value. Call
/// usp_GetNextDocumentNumber, which does it in one atomic UPDATE.
/// </summary>
public class NumberSeries
{
    public int NumberSeriesId { get; set; }
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Null means the series never resets.</summary>
    public int? FinancialYearId { get; set; }

    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public string Separator { get; set; } = "/";
    public bool IncludeYearCode { get; set; } = true;
    public int CurrentNumber { get; set; }
    public byte PaddingLength { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }

    public FinancialYear? FinancialYear { get; set; }
}

/// <summary>Maps to AppSettings - key/value configuration read at startup.</summary>
public class AppSetting
{
    public int SettingId { get; set; }
    public string SettingKey { get; set; } = string.Empty;
    public string? SettingValue { get; set; }
    public SettingDataType DataType { get; set; } = SettingDataType.String;
    /// <summary>Groups settings on the admin screen - "Billing", "Print". Not an item group.</summary>
    public string? Category { get; set; }
    public string? Description { get; set; }

    /// <summary>Internal toggles the settings screen must not expose.</summary>
    public bool IsSystem { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

/// <summary>
/// Maps to ModuleMaster - one row is one sidebar entry.
///
/// The navigation used to be a TypeScript array in the web bundle, so adding a
/// screen meant a redeploy. Here the route, its label, its group and its sort
/// position are data, and inserting a row is the whole deployment.
///
/// Two ordering columns, not one: <see cref="ModuleHeadDisplayOrder"/> sorts the
/// groups against each other, <see cref="ModuleDisplayOrder"/> sorts the items
/// inside a group. A single global sequence would force every row in a group to
/// be renumbered whenever a group moved.
/// </summary>
public class ModuleMaster
{
    public int ModuleId { get; set; }

    /// <summary>The frontend route, leading slash included: '/items'.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>What the sidebar prints for this entry.</summary>
    public string ModuleDisplayName { get; set; } = string.Empty;

    /// <summary>Internal group key; stays stable when the heading is retitled.</summary>
    public string ModuleHeadName { get; set; } = string.Empty;

    /// <summary>The heading the sidebar prints above the group.</summary>
    public string ModuleHeadDisplayName { get; set; } = string.Empty;

    public int ModuleHeadDisplayOrder { get; set; }
    public int ModuleDisplayOrder { get; set; }

    /// <summary>The group's sequence, repeated on every member row.</summary>
    public int SetGroupIndex { get; set; }

    /// <summary>Lucide React icon name, e.g. 'Package'. Null falls back in the UI.</summary>
    public string? IconName { get; set; }

    /// <summary>
    /// Entries are retired, never removed: a hard delete would silently change
    /// what the menu offers with nothing left to explain why.
    /// </summary>
    public bool IsDeletedTransaction { get; set; }

    public DateTime CreatedDate { get; set; }
}

/// <summary>
/// Maps to AuditLogs.
///
/// Written by an EF Core SaveChanges interceptor rather than by database
/// triggers: the interceptor knows the authenticated user, their IP and the
/// request id, none of which a trigger can see.
/// </summary>
public class AuditLog
{
    public long AuditLogId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string? PrimaryKeyValue { get; set; }
    public AuditAction Action { get; set; }
    public string? OldValues { get; set; }      // JSON
    public string? NewValues { get; set; }      // JSON
    public string? ChangedColumns { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public string? RequestId { get; set; }
    public DateTime LoggedAt { get; set; }
}
