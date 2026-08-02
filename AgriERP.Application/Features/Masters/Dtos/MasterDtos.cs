using AgriERP.Domain.Enums;
using AgriERP.Shared.Models;

namespace AgriERP.Application.Features.Masters.Dtos;

/* ============================== ItemSubGroup ============================== */

public class ItemSubGroupListDto
{
    public int ItemSubGroupId { get; set; }
    public string ItemSubGroupCode { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;
    public int ItemGroupId { get; set; }
    public string? ItemGroupName { get; set; }
    public int? ParentItemSubGroupId { get; set; }
    public string? ParentItemSubGroupName { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
}

public class ItemSubGroupDto : ItemSubGroupListDto
{
    public string? IconName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveItemSubGroupRequest
{
    public string ItemSubGroupCode { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;

    /// <summary>
    /// Which item group this sub-group sits under. Required when there is no
    /// parent; when there is, it is inherited from the parent and any value
    /// sent here must agree with it - "Vegetable Seeds" cannot sit under
    /// Fertilizers Master while its parent "Seeds" sits under Seed Master.
    /// </summary>
    public int ItemGroupId { get; set; }

    public int? ParentItemSubGroupId { get; set; }
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ItemSubGroupQueryParameters : QueryParameters
{
    public int? ParentItemSubGroupId { get; set; }

    /// <summary>True returns only top-level itemSubGroups; the seed sub-types are excluded.</summary>
    public bool? RootOnly { get; set; }
}

/* ============================== Company ============================== */

public class CompanyListDto
{
    public int CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? City { get; set; }
    public string? StateName { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
}

public class CompanyDto : CompanyListDto
{
    public string? Address { get; set; }
    public int? StateId { get; set; }
    public string? Pincode { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? LogoPath { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveCompanyRequest
{
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ContactPerson { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CompanyQueryParameters : QueryParameters
{
    public int? StateId { get; set; }
}

/* ============================== Supplier ============================== */

public class SupplierListDto
{
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? City { get; set; }
    public string? StateName { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public int PaymentTermDays { get; set; }
    public decimal CreditLimit { get; set; }
    public bool IsActive { get; set; }
}

public class SupplierDto : SupplierListDto
{
    public string? PanNumber { get; set; }
    public string? Address { get; set; }
    public int? StateId { get; set; }
    public string? Pincode { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public decimal OpeningBalance { get; set; }
    public BalanceType OpeningBalanceType { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Derived from vw_SupplierOutstanding, never a stored column.</summary>
    public decimal OutstandingAmount { get; set; }
}

public class SaveSupplierRequest
{
    /// <summary>Left blank on create, the API allocates SUP-00001 from the number series.</summary>
    public string? SupplierCode { get; set; }

    public string SupplierName { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    public int PaymentTermDays { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal OpeningBalance { get; set; }
    public BalanceType OpeningBalanceType { get; set; } = BalanceType.CR;
    public DateTime? OpeningBalanceDate { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SupplierQueryParameters : QueryParameters
{
    public int? StateId { get; set; }
    public string? City { get; set; }

    /// <summary>True returns only suppliers with money still owed to them.</summary>
    public bool? HasOutstanding { get; set; }
}

/* ============================== Customer ============================== */

public class CustomerListDto
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Village { get; set; }
    public string? Mobile { get; set; }
    public string? GstNumber { get; set; }
    public CustomerType CustomerType { get; set; }
    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }
    public bool IsActive { get; set; }
}

public class CustomerDto : CustomerListDto
{
    public string? FatherName { get; set; }
    public string? AlternateMobile { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? StateName { get; set; }
    public string? Pincode { get; set; }
    public decimal OpeningBalance { get; set; }
    public BalanceType OpeningBalanceType { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Derived from vw_CustomerOutstanding, never a stored column.</summary>
    public decimal OutstandingAmount { get; set; }
    public DateTime? LastInvoiceDate { get; set; }
    public int? OldestUnpaidAgeDays { get; set; }
}

public class SaveCustomerRequest
{
    /// <summary>Left blank on create, the API allocates CUS-00001 from the number series.</summary>
    public string? CustomerCode { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string? FatherName { get; set; }
    public string? Village { get; set; }
    public string? Mobile { get; set; }
    public string? AlternateMobile { get; set; }
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? Pincode { get; set; }
    public CustomerType CustomerType { get; set; } = CustomerType.Retail;
    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }
    public decimal OpeningBalance { get; set; }
    public BalanceType OpeningBalanceType { get; set; } = BalanceType.DR;
    public DateTime? OpeningBalanceDate { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomerQueryParameters : QueryParameters
{
    public string? Village { get; set; }
    public CustomerType? CustomerType { get; set; }
    public bool? HasOutstanding { get; set; }
}

/* ============================== Unit ============================== */

public class UnitDto
{
    public int UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AllowDecimal { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}

public class SaveUnitRequest
{
    public string UnitCode { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AllowDecimal { get; set; } = true;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UnitQueryParameters : QueryParameters
{
}
