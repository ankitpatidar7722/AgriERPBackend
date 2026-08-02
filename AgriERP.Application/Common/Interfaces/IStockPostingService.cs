using AgriERP.Domain.Enums;

namespace AgriERP.Application.Common.Interfaces;

/// <summary>One stock movement to be written to the journal.</summary>
public record StockMovement(
    StockTransactionTypeId TransactionType,
    DateTime TransactionDate,
    int ItemId,
    long BatchId,
    int LocationId,
    decimal Quantity,
    decimal Rate,
    string ReferenceType,
    long ReferenceId,
    long? ReferenceDetailId,
    string ReferenceNumber,
    string? Remarks = null);

/// <summary>
/// The only supported way to move stock.
///
/// Everything here goes through usp_PostStockTransaction, which validates
/// the movement, updates the batch balance and appends the journal row inside
/// one transaction. Writing those three separately from application code is
/// exactly how stock and ledger drift apart.
/// </summary>
public interface IStockPostingService
{
    /// <summary>
    /// Posts one movement. Throws BusinessRuleException when the movement
    /// would take a batch negative and the item does not allow it.
    /// </summary>
    Task<long> PostAsync(StockMovement movement, CancellationToken ct = default);

    Task PostManyAsync(IEnumerable<StockMovement> movements, CancellationToken ct = default);

    /// <summary>
    /// Cancels a document by appending reversing rows, never by deleting.
    /// Calling it twice is a no-op: rows already reversed are skipped.
    /// </summary>
    Task<int> ReverseDocumentAsync(
        string referenceType, long referenceId, string? remarks = null, CancellationToken ct = default);
}
