using System.Linq.Expressions;

namespace AgriERP.Application.Common.Interfaces;

/// <summary>
/// Generic repository.
///
/// <see cref="Query"/> deliberately returns IQueryable. The purist position is
/// that a repository should hide IQueryable behind named methods, but with 46
/// entities each needing search + filter + sort + page, that produces hundreds
/// of near-identical methods and pushes paging into memory the first time
/// someone forgets. Exposing IQueryable keeps composition in the service layer
/// and the whole query in SQL.
///
/// The trade-off is real: a service CAN write an inefficient query. That is
/// caught by review and by the SQL logged in Development, and is a better
/// problem than a repository nobody can extend.
/// </summary>
public interface IRepository<T> where T : class
{
    /// <summary>
    /// Tracking is off by default. Read paths vastly outnumber write paths in
    /// an ERP, and change-tracking a 200-row item grid is wasted work.
    /// Pass tracking: true when you intend to modify the result.
    /// </summary>
    IQueryable<T> Query(bool tracking = false);

    Task<T?> GetByIdAsync(CancellationToken ct = default, params object[] keyValues);

    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, bool tracking = false, CancellationToken ct = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    Task AddAsync(T entity, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    void Update(T entity);

    void Remove(T entity);

    void RemoveRange(IEnumerable<T> entities);
}
