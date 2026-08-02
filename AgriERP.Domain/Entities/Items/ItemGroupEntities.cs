using AgriERP.Domain.Common;

namespace AgriERP.Domain.Entities.Items;

/// <summary>
/// Maps to ItemGroupMaster - the KIND of item: Product, Fertilizer, Seed,
/// Other.
///
/// A shop does not enter a pesticide the way it enters a seed. A pesticide
/// needs a technical name and a CIB licence number; a seed needs a germination
/// percentage and a lot number; a sprayer needs neither. The group is what the
/// item form asks, so that the operator is never shown two thirds of a screen
/// that does not apply.
/// </summary>
public class ItemGroup : MasterEntity
{
    public int ItemGroupId { get; set; }
    public string ItemGroupCode { get; set; } = string.Empty;   // PRDGRP
    public string ItemGroupName { get; set; } = string.Empty;   // "Product Master"

    /// <summary>
    /// Feeds the item code, and each group counts separately: PRD-000001 and
    /// SED-000001 exist at the same time and the code says what the item is.
    /// </summary>
    public string ItemCodePrefix { get; set; } = string.Empty;  // PRD

    public string? Description { get; set; }
    public string? IconName { get; set; }
    public int DisplayOrder { get; set; }

    public ICollection<ItemGroupField> Fields { get; set; } = new List<ItemGroupField>();
    public ICollection<Masters.ItemSubGroup> SubGroups { get; set; } = new List<Masters.ItemSubGroup>();
}

/// <summary>
/// Maps to ItemGroupFieldMaster - one row per field per group. This is the
/// form definition: which fields a group shows, what to label them, in what
/// order, and how to validate them.
/// </summary>
public class ItemGroupField : MasterEntity
{
    public int ItemGroupFieldId { get; set; }
    public int ItemGroupId { get; set; }
    public ItemGroup? ItemGroup { get; set; }

    /// <summary>
    /// When <see cref="IsStoredOnItem"/> is true this is a real column name on
    /// ItemMaster. The service refuses to save a definition naming a column
    /// that does not exist, so a typo cannot become a field that silently
    /// discards whatever is typed into it.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    public string FieldDisplayName { get; set; } = string.Empty;
    public string? HelpText { get; set; }

    /// <summary>text | number | decimal | select | checkbox | date | textarea</summary>
    public string FieldType { get; set; } = "text";

    /// <summary>
    /// True  - the value lives in the ItemMaster COLUMN named by FieldName,
    ///         keeping its real type, CHECK constraints and foreign keys.
    ///         Everything billing, FEFO and GST depend on is stored this way.
    /// False - the value lives in <see cref="ItemMasterDetail"/> as text,
    ///         because a typed column would be NULL for three groups in four.
    ///
    /// One place or the other, never both. The system this pattern is modelled
    /// on stores every value twice and has nothing keeping the copies in step.
    /// </summary>
    public bool IsStoredOnItem { get; set; } = true;

    public bool IsDisplay { get; set; } = true;
    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>How many of the form's four columns this field occupies.</summary>
    public byte ColumnSpan { get; set; } = 1;
    public string? SectionName { get; set; }

    public string? DefaultValue { get; set; }

    /// <summary>
    /// The NAME of a lookup list - "units", "companies", "gstslabs" - resolved
    /// by the API against a fixed whitelist. Never SQL: the source system keeps
    /// SELECT statements in this column and concatenates them at runtime, which
    /// is unversioned code living in data and an injection surface besides.
    /// </summary>
    public string? LookupSource { get; set; }

    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? UnitLabel { get; set; }
}

/// <summary>
/// Maps to ItemMasterDetails - the value of one group-specific field for
/// one item.
///
/// Only fields with <see cref="ItemGroupField.IsStoredOnItem"/> false land
/// here: seed germination, fertilizer N-P-K, equipment warranty. Anything the
/// rest of the application computes with keeps a typed column instead.
/// </summary>
public class ItemMasterDetail
{
    public long ItemDetailId { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>
    /// A real foreign key, unlike the source system where the equivalent column
    /// is 0 on all 51,694 rows and values are matched to their definition by
    /// field NAME - which orphans every value the moment a field is renamed.
    /// </summary>
    public int ItemGroupFieldId { get; set; }
    public ItemGroupField? Field { get; set; }

    public string? FieldValue { get; set; }

    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}
