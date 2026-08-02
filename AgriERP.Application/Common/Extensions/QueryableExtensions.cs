using AgriERP.Shared.Models;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AgriERP.Application.Common.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Pages, then projects through AutoMapper.
    ///
    /// ProjectTo rather than Map: it rewrites the projection into the SQL
    /// SELECT list, so only the DTO's columns leave the database. Mapping after
    /// materialisation would pull every column of every entity - including the
    /// 1000-character item Description - to render a grid that shows six
    /// fields.
    /// </summary>
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TSource, TDto>(
        this IQueryable<TSource> query,
        IConfigurationProvider mapperConfiguration,
        QueryParameters parameters,
        CancellationToken ct = default)
    {
        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<TDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .Skip(parameters.Skip)
            .Take(parameters.PageSize)
            .ProjectTo<TDto>(mapperConfiguration)
            .ToListAsync(ct);

        return PagedResult<TDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    /// <summary>
    /// Counts and pages in the database, then projects. The count runs before
    /// Skip/Take so the pager knows the true total.
    /// </summary>
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TSource, TDto>(
        this IQueryable<TSource> query,
        Expression<Func<TSource, TDto>> projection,
        QueryParameters parameters,
        CancellationToken ct = default)
    {
        var totalCount = await query.CountAsync(ct);

        if (totalCount == 0)
            return PagedResult<TDto>.Empty(parameters.Page, parameters.PageSize);

        var items = await query
            .Skip(parameters.Skip)
            .Take(parameters.PageSize)
            .Select(projection)
            .ToListAsync(ct);

        return PagedResult<TDto>.Create(items, parameters.Page, parameters.PageSize, totalCount);
    }

    /// <summary>Ordering helper so services do not repeat the desc/asc ternary on every sort key.</summary>
    public static IOrderedQueryable<T> OrderByDirection<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        bool descending)
        => descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);

    public static IOrderedQueryable<T> ThenByDirection<T, TKey>(
        this IOrderedQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        bool descending)
        => descending ? query.ThenByDescending(keySelector) : query.ThenBy(keySelector);

    /// <summary>
    /// Applies a predicate only when <paramref name="condition"/> holds, so
    /// optional filters read as a flat chain rather than nested ifs.
    /// </summary>
    public static IQueryable<T> WhereIf<T>(
        this IQueryable<T> query,
        bool condition,
        Expression<Func<T, bool>> predicate)
        => condition ? query.Where(predicate) : query;
}
