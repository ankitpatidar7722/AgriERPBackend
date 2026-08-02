namespace AgriERP.Shared.Models;

/// <summary>
/// Base for every list endpoint's query string: paging, search, sorting and
/// the active/inactive filter.
///
/// PageSize is clamped rather than trusted. Without the ceiling, a request for
/// ?pageSize=100000 against the item list becomes an accidental
/// denial-of-service on a shop running SQL Server Express on the counter PC.
/// </summary>
public abstract class QueryParameters
{
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 25;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Free-text search. Which columns it covers is per-module.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Sort key from the module's whitelist, not a column name. Unknown keys
    /// fall back to the module default instead of erroring, so a stale
    /// bookmark from an older UI build still returns data.
    /// </summary>
    public string? SortBy { get; set; }

    public bool SortDescending { get; set; }

    /// <summary>Null returns both active and inactive rows.</summary>
    public bool? IsActive { get; set; }

    public int Skip => (Page - 1) * PageSize;

    public string? NormalizedSearch =>
        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}
