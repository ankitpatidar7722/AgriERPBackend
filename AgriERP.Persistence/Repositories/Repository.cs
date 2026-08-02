using AgriERP.Application.Common.Interfaces;
using AgriERP.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AgriERP.Persistence.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    private readonly AgriErpDbContext _context;
    private readonly DbSet<T> _set;

    public Repository(AgriErpDbContext context)
    {
        _context = context;
        _set = context.Set<T>();
    }

    public IQueryable<T> Query(bool tracking = false)
        => tracking ? _set : _set.AsNoTracking();

    public async Task<T?> GetByIdAsync(CancellationToken ct = default, params object[] keyValues)
        => await _set.FindAsync(keyValues, ct);

    public async Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate, bool tracking = false, CancellationToken ct = default)
        => await Query(tracking).FirstOrDefaultAsync(predicate, ct);

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _set.AsNoTracking().AnyAsync(predicate, ct);

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => predicate is null
            ? await _set.AsNoTracking().CountAsync(ct)
            : await _set.AsNoTracking().CountAsync(predicate, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await _set.AddAsync(entity, ct);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        => await _set.AddRangeAsync(entities, ct);

    public void Update(T entity) => _set.Update(entity);

    public void Remove(T entity) => _set.Remove(entity);

    public void RemoveRange(IEnumerable<T> entities) => _set.RemoveRange(entities);
}
