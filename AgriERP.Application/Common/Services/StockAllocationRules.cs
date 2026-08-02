namespace AgriERP.Application.Common.Services;

/// <summary>A batch offered to the allocator, already filtered and ordered.</summary>
public record AllocationCandidate(
    long BatchId,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal AvailableQty,
    decimal CostRate,
    decimal Mrp,
    decimal SellingRate);

/// <summary>
/// The FEFO picking rule, as a pure function.
///
/// Split out from BatchAllocator so the rule can be tested without a database:
/// the querying (which batches are sellable, at which location, not expired)
/// belongs to the service, but *how much comes from which batch* is arithmetic
/// and deserves to be verifiable on its own.
/// </summary>
public static class FefoAllocation
{
    /// <summary>
    /// Consumes <paramref name="candidates"/> in the order given until the
    /// requested quantity is met.
    ///
    /// The caller is responsible for ordering by expiry - this function honours
    /// whatever order it receives, which is what makes it testable and what
    /// lets a caller override FEFO for a hand-picked batch.
    /// </summary>
    /// <param name="allowShortfall">
    /// When true, any unmet quantity is pushed onto the last candidate, taking
    /// it negative. Only items with AllowNegativeStock reach this path.
    /// </param>
    public static IReadOnlyList<(AllocationCandidate Candidate, decimal Quantity)> Allocate(
        IReadOnlyList<AllocationCandidate> candidates,
        decimal requiredQuantity,
        bool allowShortfall)
    {
        if (requiredQuantity <= 0)
            return Array.Empty<(AllocationCandidate, decimal)>();

        var allocations = new List<(AllocationCandidate, decimal)>();
        var remaining = requiredQuantity;

        foreach (var candidate in candidates)
        {
            if (remaining <= 0) break;
            if (candidate.AvailableQty <= 0) continue;

            var take = Math.Min(remaining, candidate.AvailableQty);
            allocations.Add((candidate, take));
            remaining -= take;
        }

        if (remaining > 0 && allowShortfall && allocations.Count > 0)
        {
            // The shortfall lands on the last batch touched rather than being
            // spread: one batch going negative is traceable, several fractional
            // negatives are not.
            var (lastCandidate, lastQuantity) = allocations[^1];
            allocations[^1] = (lastCandidate, lastQuantity + remaining);
            remaining = 0;
        }

        return remaining > 0 && !allowShortfall
            // Caller decides what to do; returning a partial allocation would
            // let an under-filled bill post silently.
            ? Array.Empty<(AllocationCandidate, decimal)>()
            : allocations;
    }

    /// <summary>Total sellable quantity across the candidates.</summary>
    public static decimal TotalAvailable(IReadOnlyList<AllocationCandidate> candidates)
        => candidates.Sum(candidate => candidate.AvailableQty);
}

/// <summary>One purchase line's inputs to the landed-cost calculation.</summary>
public record CostableLine(decimal TaxableAmount, decimal Quantity, decimal FreeQuantity);

/// <summary>
/// Landed-cost apportionment, as a pure function.
///
/// Split out of PurchaseService for the same reason as FEFO: this arithmetic
/// decides the cost of every batch and therefore the profit on every sale, so
/// it should be verifiable without standing up a database.
/// </summary>
public static class LandedCost
{
    /// <summary>
    /// Spreads <paramref name="chargesToSpread"/> (freight plus other charges)
    /// across lines in proportion to taxable value, then divides each line by
    /// its TOTAL quantity including free goods.
    ///
    /// Both halves matter. Ignoring freight understates cost on every bulky
    /// consignment; dividing by paid quantity alone ignores that a "10 + 1
    /// free" scheme is exactly what makes the deal profitable. GST is excluded
    /// because it is input credit, not cost.
    /// </summary>
    public static IReadOnlyList<decimal> Apportion(
        IReadOnlyList<CostableLine> lines,
        decimal chargesToSpread)
    {
        var totalTaxable = lines.Sum(line => line.TaxableAmount);
        var rates = new decimal[lines.Count];

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            var share = totalTaxable > 0 && chargesToSpread > 0
                ? Math.Round(chargesToSpread * line.TaxableAmount / totalTaxable, 2,
                             MidpointRounding.AwayFromZero)
                : 0m;

            var costableQty = line.Quantity + line.FreeQuantity;

            rates[index] = costableQty > 0
                // Four decimals: a rate rounded to paise loses real money once
                // multiplied back out over a few hundred units.
                ? Math.Round((line.TaxableAmount + share) / costableQty, 4,
                             MidpointRounding.AwayFromZero)
                : 0m;
        }

        return rates;
    }
}
