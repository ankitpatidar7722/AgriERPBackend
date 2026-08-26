namespace AgriERP.Application.Common.Interfaces;

/// <summary>
/// One transactional boundary per request.
///
/// Explicit transactions matter here more than in most applications: posting a
/// sale writes the invoice, its lines, the stock journal and the batch
/// balances. Any of those landing without the others leaves stock disagreeing
/// with the bills - the single worst failure mode in an inventory system.
/// </summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Detaches every tracked entity. Bulk operations that call an entity's
    /// CreateAsync once per row use this: if one row's SaveChanges throws, its
    /// still-Added entity must not be carried into the next row's save.
    /// </summary>
    void ClearTracking();

    Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="action"/> inside a transaction, committing on
    /// success and rolling back on any exception.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken ct = default);
}

public interface IAppTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
