using AgriERP.Application.Common.Interfaces;
using AgriERP.Domain.Entities.Items;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Features.Items;

/* ------------------------------- contracts ------------------------------- */

public class ItemGroupDto
{
    public int ItemGroupId { get; set; }
    public string ItemGroupCode { get; set; } = string.Empty;
    public string ItemGroupName { get; set; } = string.Empty;
    public string ItemCodePrefix { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public int DisplayOrder { get; set; }
    public int SubGroupCount { get; set; }
    public int ItemCount { get; set; }
}

public class ItemGroupFieldDto
{
    public int ItemGroupFieldId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string FieldDisplayName { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public string FieldType { get; set; } = "text";

    /// <summary>
    /// The client does not need to care where a value is kept, but it does
    /// need to send it back the same way, so this travels with the definition.
    /// </summary>
    public bool IsStoredOnItem { get; set; }

    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public int DisplayOrder { get; set; }
    public byte ColumnSpan { get; set; }
    public string? SectionName { get; set; }
    public string? DefaultValue { get; set; }
    public string? LookupSource { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? UnitLabel { get; set; }
}

/// <summary>Everything needed to draw the entry form for one item group.</summary>
public class ItemFormDefinitionDto
{
    public ItemGroupDto Group { get; set; } = new();
    public IReadOnlyList<string> Sections { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ItemGroupFieldDto> Fields { get; set; } = Array.Empty<ItemGroupFieldDto>();
}

public interface IItemGroupService
{
    Task<IReadOnlyList<ItemGroupDto>> GetGroupsAsync(CancellationToken ct = default);
    Task<ItemFormDefinitionDto?> GetFormAsync(int itemGroupId, CancellationToken ct = default);
}

/* -------------------------------- service -------------------------------- */

/// <summary>
/// Serves the item groups and, for any one of them, the form definition the
/// client renders from.
///
/// The service is read-only on purpose. Field definitions are schema-shaped
/// data - adding one changes what the application stores - so they are edited
/// through a migration script that can be reviewed and re-run, not through a
/// screen that lets a busy shopkeeper delete the GST field on a Tuesday.
/// </summary>
public class ItemGroupService : IItemGroupService
{
    private readonly IUnitOfWork _uow;

    /// <summary>
    /// The only lookup names a field definition may use.
    ///
    /// This exists because the system this design comes from stores executable
    /// SELECT statements in the field table and concatenates them at runtime.
    /// That is unversioned code living in data, and an injection surface: a
    /// write to one row becomes arbitrary SQL on the next page load. A name
    /// checked against a fixed list cannot do that.
    /// </summary>
    private static readonly HashSet<string> AllowedLookups = new(StringComparer.OrdinalIgnoreCase)
    {
        "subgroups", "companies", "units", "gstslabs", "hsncodes",
        "locations", "fertilizerform", "season"
    };

    public ItemGroupService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<ItemGroupDto>> GetGroupsAsync(CancellationToken ct = default)
    {
        // Hoisted out of the projection on purpose: Query() takes an optional
        // parameter, and an expression tree cannot contain a call that uses
        // one. Assigning them first keeps the counts as correlated sub-queries
        // in the same round trip rather than an N+1.
        var subGroups = _uow.Repository<Domain.Entities.Masters.ItemSubGroup>().Query();
        var items = _uow.Repository<Item>().Query();

        return await _uow.Repository<ItemGroup>().Query()
            .AsNoTracking()
            .Where(g => g.IsActive)
            .OrderBy(g => g.DisplayOrder)
            .Select(g => new ItemGroupDto
            {
                ItemGroupId    = g.ItemGroupId,
                ItemGroupCode  = g.ItemGroupCode,
                ItemGroupName  = g.ItemGroupName,
                ItemCodePrefix = g.ItemCodePrefix,
                Description    = g.Description,
                IconName       = g.IconName,
                DisplayOrder   = g.DisplayOrder,
                SubGroupCount  = subGroups.Count(s => s.ItemGroupId == g.ItemGroupId && s.IsActive),
                ItemCount      = items.Count(i => i.ItemGroupId == g.ItemGroupId && i.IsActive)
            })
            .ToListAsync(ct);
    }

    public async Task<ItemFormDefinitionDto?> GetFormAsync(int itemGroupId, CancellationToken ct = default)
    {
        var group = await _uow.Repository<ItemGroup>().Query()
            .AsNoTracking()
            .Where(g => g.ItemGroupId == itemGroupId)
            .Select(g => new ItemGroupDto
            {
                ItemGroupId    = g.ItemGroupId,
                ItemGroupCode  = g.ItemGroupCode,
                ItemGroupName  = g.ItemGroupName,
                ItemCodePrefix = g.ItemCodePrefix,
                Description    = g.Description,
                IconName       = g.IconName,
                DisplayOrder   = g.DisplayOrder
            })
            .FirstOrDefaultAsync(ct);

        if (group is null) return null;

        var fields = await _uow.Repository<ItemGroupField>().Query()
            .AsNoTracking()
            .Where(f => f.ItemGroupId == itemGroupId && f.IsDisplay && f.IsActive)
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new ItemGroupFieldDto
            {
                ItemGroupFieldId = f.ItemGroupFieldId,
                FieldName        = f.FieldName,
                FieldDisplayName = f.FieldDisplayName,
                HelpText         = f.HelpText,
                FieldType        = f.FieldType,
                IsStoredOnItem   = f.IsStoredOnItem,
                IsRequired       = f.IsRequired,
                IsReadOnly       = f.IsReadOnly,
                DisplayOrder     = f.DisplayOrder,
                ColumnSpan       = f.ColumnSpan,
                SectionName      = f.SectionName,
                DefaultValue     = f.DefaultValue,
                LookupSource     = f.LookupSource,
                MinValue         = f.MinValue,
                MaxValue         = f.MaxValue,
                MaxLength        = f.MaxLength,
                UnitLabel        = f.UnitLabel
            })
            .ToListAsync(ct);

        // A definition naming an unknown lookup would render as a select with
        // no options - a dead control the operator cannot get past on a
        // required field. Dropping the name degrades it to a plain text box,
        // which at least accepts input, and the mismatch shows up in the API
        // test rather than as a stuck form at the counter.
        foreach (var field in fields)
        {
            if (field.LookupSource is not null && !AllowedLookups.Contains(field.LookupSource))
            {
                field.LookupSource = null;
                field.FieldType = field.FieldType == "select" ? "text" : field.FieldType;
            }
        }

        return new ItemFormDefinitionDto
        {
            Group    = group,
            Sections = fields.Select(f => f.SectionName ?? "Details").Distinct().ToList(),
            Fields   = fields
        };
    }
}
