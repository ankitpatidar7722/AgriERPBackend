using AgriERP.Application.Common.Interfaces;
using AgriERP.Persistence.Context;
// The simple ExecuteAsync(Func<Task<T>>) overload is an extension method in
// this namespace, not a member of IExecutionStrategy.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;

namespace AgriERP.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AgriErpDbContext _context;
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    public UnitOfWork(AgriErpDbContext context) => _context = context;

    public IRepository<T> Repository<T>() where T : class
        => (IRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new Repository<T>(_context));

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public void ClearTracking() => _context.ChangeTracker.Clear();

    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => new EfTransaction(await _context.Database.BeginTransactionAsync(ct));

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken ct = default)
    {
        // The connection is configured with EnableRetryOnFailure, and a
        // retrying strategy refuses a manually-opened transaction unless the
        // whole unit is wrapped like this - otherwise a retry would replay only
        // part of the work. Posting an invoice must be all-or-nothing.
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await action(ct);
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private sealed class EfTransaction : IAppTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfTransaction(IDbContextTransaction transaction) => _transaction = transaction;

        public Task CommitAsync(CancellationToken ct = default) => _transaction.CommitAsync(ct);

        public Task RollbackAsync(CancellationToken ct = default) => _transaction.RollbackAsync(ct);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
