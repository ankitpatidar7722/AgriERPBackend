namespace AgriERP.Application.Features.Modules.Dtos;

/// <summary>One sidebar entry, flat, as stored in ModuleMaster.</summary>
public class ModuleDto
{
    public int ModuleId { get; set; }

    /// <summary>The frontend route the entry navigates to: '/items'.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>The text the sidebar prints.</summary>
    public string ModuleDisplayName { get; set; } = string.Empty;

    /// <summary>Lucide React icon name, e.g. 'Package'. Null falls back in the UI.</summary>
    public string? IconName { get; set; }

    public int ModuleDisplayOrder { get; set; }
}

/// <summary>
/// A heading and the entries under it.
///
/// Grouped server-side rather than shipping a flat list for the client to
/// bucket: the ordering rules (groups by ModuleHeadDisplayOrder, items by
/// ModuleDisplayOrder) belong in one place, and every consumer then renders the
/// same menu in the same order without re-deriving it.
/// </summary>
public class SidebarGroupDto
{
    /// <summary>Internal group key - stable identity, safe as a React key.</summary>
    public string ModuleHeadName { get; set; } = string.Empty;

    /// <summary>The heading the sidebar prints.</summary>
    public string ModuleHeadDisplayName { get; set; } = string.Empty;

    public int ModuleHeadDisplayOrder { get; set; }
    public int SetGroupIndex { get; set; }

    public IReadOnlyList<ModuleDto> Modules { get; set; } = Array.Empty<ModuleDto>();
}
