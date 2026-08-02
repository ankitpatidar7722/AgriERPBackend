namespace AgriERP.Shared.Models;

/// <summary>
/// One page of results plus the counts a data grid needs to render its pager.
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
        => new() { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };

    public static PagedResult<T> Empty(int page, int pageSize)
        => new() { Items = Array.Empty<T>(), Page = page, PageSize = pageSize, TotalCount = 0 };
}
