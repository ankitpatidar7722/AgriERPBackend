using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Domain.Entities.Items;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Application.Common.Services;

/// <summary>One batch and how much of the requested quantity comes from it.</summary>
public record BatchAllocation(
    long BatchId,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal Quantity,
    decimal CostRate,
    decimal Mrp,
    decimal SellingRate);

public interface IBatchAllocator
{
    /// <summary>
    /// Picks batches for a sale using FEFO. May return several allocations for
    /// one request when the quantity spans batches.
    /// </summary>
    Task<IReadOnlyList<BatchAllocation>> AllocateAsync(
        int itemId, int locationId, decimal quantity, CancellationToken ct = default);

    /// <summary>Uses a batch the operator chose explicitly, still checking stock and expiry.</summary>
    Task<BatchAllocation> AllocateFromBatchAsync(
        long batchId, decimal quantity, CancellationToken ct = default);
}

public class BatchAllocator : IBatchAllocator
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public BatchAllocator(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<IReadOnlyList<BatchAllocation>> AllocateAsync(
        int itemId, int locationId, decimal quantity, CancellationToken ct = default)
    {
        if (quantity <= 0)
            throw new ValidationException("Quantity", "Quantity must be greater than zero.");

        var item = await _uow.Repository<Item>()
            .FirstOrDefaultAsync(p => p.ItemId == itemId && !p.IsDeleted, tracking: false, ct)
            ?? throw new NotFoundException("Item", itemId);

        var today = _clock.Today;

        var candidates = await _uow.Repository<ItemBatch>().Query()
            .Where(b => b.ItemId == itemId
                        && b.LocationId == locationId
                        && b.IsActive
                        && b.CurrentQty > 0
                        // Expired stock must never be picked automatically.
                        // Selling an expired pesticide is a licensing problem,
                        // not merely a bookkeeping one.
                        && (b.ExpiryDate == null || b.ExpiryDate >= today))
            // FEFO: earliest expiry first, undated stock last. Plain FIFO would
            // quietly age dated stock into a write-off.
            .OrderBy(b => b.ExpiryDate == null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchId)
            .Select(b => new AllocationCandidate(
                b.BatchId, b.BatchNumber, b.ExpiryDate, b.CurrentQty,
                b.PurchaseRate, b.Mrp, b.SellingRate))
            .ToListAsync(ct);

        var available = FefoAllocation.TotalAvailable(candidates);

        if (available < quantity && !item.AllowNegativeStock)
            throw new BusinessRuleException(
                $"Insufficient sellable stock for {item.ItemName}. " +
                $"Available {available:N3}, required {quantity:N3}. " +
                "Expired batches are excluded from this figure.",
                "INSUFFICIENT_STOCK");

        // The picking rule itself lives in FefoAllocation so it can be tested
        // without a database; this method only decides WHICH batches are
        // eligible to be offered to it.
        var picked = FefoAllocation.Allocate(candidates, quantity, item.AllowNegativeStock);

        if (picked.Count > 0)
        {
            return picked
                .Select(item => new BatchAllocation(
                    item.Candidate.BatchId, item.Candidate.BatchNumber, item.Candidate.ExpiryDate,
                    item.Quantity, item.Candidate.CostRate, item.Candidate.Mrp,
                    item.Candidate.SellingRate))
                .ToList();
        }

        // Nothing sellable at all, and the item tolerates negative stock:
        // fall back to any batch so the movement still has somewhere to land.
        var fallback = await _uow.Repository<ItemBatch>().Query()
            .Where(b => b.ItemId == itemId && b.LocationId == locationId)
            .OrderBy(b => b.BatchId)
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException(
                $"{item.ItemName} has no batch at this location to sell from.",
                "NO_BATCH");

        return new List<BatchAllocation>
        {
            new(fallback.BatchId, fallback.BatchNumber, fallback.ExpiryDate, quantity,
                fallback.PurchaseRate, fallback.Mrp, fallback.SellingRate),
        };
    }

    public async Task<BatchAllocation> AllocateFromBatchAsync(
        long batchId, decimal quantity, CancellationToken ct = default)
    {
        var batch = await _uow.Repository<ItemBatch>().Query()
            .Include(b => b.Item)
            .FirstOrDefaultAsync(b => b.BatchId == batchId, ct)
            ?? throw new NotFoundException("Batch", batchId);

        var allowNegative = batch.Item?.AllowNegativeStock ?? false;

        if (batch.CurrentQty < quantity && !allowNegative)
            throw new BusinessRuleException(
                $"Batch {batch.BatchNumber} holds {batch.CurrentQty:N3}, but {quantity:N3} was requested.",
                "INSUFFICIENT_STOCK");

        // An operator may deliberately pick an expired batch to write it off,
        // but never silently: this path refuses and the caller must use a
        // stock adjustment instead.
        if (batch.ExpiryDate is { } expiry && expiry.Date < _clock.Today)
            throw new BusinessRuleException(
                $"Batch {batch.BatchNumber} expired on {expiry:dd-MM-yyyy} and cannot be sold. " +
                "Write it off with a stock adjustment instead.",
                "BATCH_EXPIRED");

        return new BatchAllocation(
            batch.BatchId, batch.BatchNumber, batch.ExpiryDate, quantity,
            batch.PurchaseRate, batch.Mrp, batch.SellingRate);
    }
}
