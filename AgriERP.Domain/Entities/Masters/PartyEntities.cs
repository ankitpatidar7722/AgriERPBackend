using AgriERP.Domain.Common;
using AgriERP.Domain.Enums;

namespace AgriERP.Domain.Entities.Masters;

/// <summary>
/// Maps to ItemSubGroupMaster. Self-referencing: "Seeds" is the parent of
/// "Vegetable Seeds", "Field Crop Seeds", "Flower Seeds" and "Fruit Seeds",
/// so the item screen can offer one broad filter or four specific ones
/// without a second table.
/// </summary>
public class ItemSubGroup : MasterEntity
{
    public int ItemSubGroupId { get; set; }
    public string ItemSubGroupCode { get; set; } = string.Empty;
    public string ItemSubGroupName { get; set; } = string.Empty;

    /// <summary>
    /// Which item group this sub-group belongs to. Fertilizer, Bio Fertilizer
    /// and Micronutrient all sit under Fertilizers Master; Seeds and its four
    /// children sit under Seed Master. An item inherits this from its
    /// sub-group when it is created.
    /// </summary>
    public int ItemGroupId { get; set; }
    public Items.ItemGroup? ItemGroup { get; set; }

    public int? ParentItemSubGroupId { get; set; }
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public int DisplayOrder { get; set; }

    public ItemSubGroup? ParentItemSubGroup { get; set; }
    public ICollection<ItemSubGroup> ChildItemSubGroups { get; set; } = new List<ItemSubGroup>();
    public ICollection<Items.Item> Items { get; set; } = new List<Items.Item>();
}

/// <summary>Maps to Companies - the manufacturer (UPL, Bayer, IFFCO).</summary>
public class Company : MasterEntity
{
    public int CompanyId { get; set; }
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
    public string? LogoPath { get; set; }
    public string? Remarks { get; set; }

    public State? State { get; set; }
    public ICollection<Items.Item> Items { get; set; } = new List<Items.Item>();
}

/// <summary>Maps to Suppliers.</summary>
public class Supplier : MasterEntity
{
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
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

    /// <summary>Net credit days; the purchase screen computes DueDate from this.</summary>
    public int PaymentTermDays { get; set; }

    public decimal CreditLimit { get; set; }

    /// <summary>
    /// Balance carried in when the shop started using this system. The CURRENT
    /// outstanding is not stored - read vw_SupplierOutstanding, which
    /// derives it from posted bills, returns and payments.
    /// </summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>CR is the normal case: we owe the supplier.</summary>
    public BalanceType OpeningBalanceType { get; set; } = BalanceType.CR;

    public DateTime? OpeningBalanceDate { get; set; }

    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? Remarks { get; set; }

    public State? State { get; set; }
}

/// <summary>Maps to Customers.</summary>
public class Customer : MasterEntity
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Optional. Father's / guardian's name, used to tell apart farmers who share a name.</summary>
    public string? FatherName { get; set; }

    /// <summary>The single most-used search field at a village agri-shop counter.</summary>
    public string? Village { get; set; }

    public string? Mobile { get; set; }
    public string? AlternateMobile { get; set; }
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public int? StateId { get; set; }
    public string? Pincode { get; set; }

    /// <summary>Decides which of the four item rates the billing screen defaults to.</summary>
    public CustomerType CustomerType { get; set; } = CustomerType.Retail;

    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }

    /// <summary>Carried-in balance only. Current dues come from vw_CustomerOutstanding.</summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>DR is the normal case: the customer owes the shop.</summary>
    public BalanceType OpeningBalanceType { get; set; } = BalanceType.DR;

    public DateTime? OpeningBalanceDate { get; set; }
    public string? Remarks { get; set; }

    public State? State { get; set; }
}
