using AgriERP.Application.Common.Services;

namespace AgriERP.Application.Tests;

/// <summary>
/// The batch picking rule.
///
/// FEFO - First Expiry, First Out - is what keeps dated agri-input stock from
/// quietly ageing into a write-off. These tests pin the arithmetic; which
/// batches are *eligible* (right location, not expired, still active) is the
/// service's job and is covered by the transaction suite against real SQL.
/// </summary>
public class FefoAllocationTests
{
    private static AllocationCandidate Batch(long id, string number, decimal available, string? expiry = null)
        => new(id, number, expiry is null ? null : DateTime.Parse(expiry), available, 100m, 150m, 130m);

    [Fact]
    public void Takes_everything_from_one_batch_when_it_covers_the_quantity()
    {
        var candidates = new[] { Batch(1, "A", 100m), Batch(2, "B", 50m) };

        var result = FefoAllocation.Allocate(candidates, 40m, allowShortfall: false);

        Assert.Single(result);
        Assert.Equal(1, result[0].Candidate.BatchId);
        Assert.Equal(40m, result[0].Quantity);
    }

    /// <summary>
    /// The case that makes one requested line become several invoice lines.
    /// Each resulting line carries its own batch and expiry, which the printed
    /// bill needs and which honest costing depends on.
    /// </summary>
    [Fact]
    public void Spans_batches_in_order_when_the_first_runs_out()
    {
        var candidates = new[] { Batch(1, "EARLY", 50m), Batch(2, "LATE", 100m) };

        var result = FefoAllocation.Allocate(candidates, 60m, allowShortfall: false);

        Assert.Equal(2, result.Count);
        Assert.Equal("EARLY", result[0].Candidate.BatchNumber);
        Assert.Equal(50m, result[0].Quantity);
        Assert.Equal("LATE", result[1].Candidate.BatchNumber);
        Assert.Equal(10m, result[1].Quantity);
        Assert.Equal(60m, result.Sum(r => r.Quantity));
    }

    [Fact]
    public void Honours_the_order_it_is_given()
    {
        // The caller sorts by expiry. This function must not re-sort, or a
        // deliberately hand-picked batch order would be silently overridden.
        var candidates = new[] { Batch(9, "LAST", 10m), Batch(1, "FIRST", 10m) };

        var result = FefoAllocation.Allocate(candidates, 15m, allowShortfall: false);

        Assert.Equal("LAST", result[0].Candidate.BatchNumber);
        Assert.Equal("FIRST", result[1].Candidate.BatchNumber);
    }

    [Fact]
    public void Returns_nothing_when_stock_is_short_and_negative_is_not_allowed()
    {
        var candidates = new[] { Batch(1, "A", 10m) };

        var result = FefoAllocation.Allocate(candidates, 50m, allowShortfall: false);

        // Empty, not partial. A partial allocation would let an under-filled
        // bill post as though it were complete.
        Assert.Empty(result);
    }

    [Fact]
    public void Pushes_the_shortfall_onto_the_last_batch_when_negative_is_allowed()
    {
        var candidates = new[] { Batch(1, "A", 10m), Batch(2, "B", 5m) };

        var result = FefoAllocation.Allocate(candidates, 50m, allowShortfall: true);

        Assert.Equal(2, result.Count);
        Assert.Equal(10m, result[0].Quantity);
        // One batch traceably negative beats several fractional negatives.
        Assert.Equal(40m, result[1].Quantity);
        Assert.Equal(50m, result.Sum(r => r.Quantity));
    }

    [Fact]
    public void Skips_batches_holding_nothing()
    {
        var candidates = new[] { Batch(1, "EMPTY", 0m), Batch(2, "STOCKED", 30m) };

        var result = FefoAllocation.Allocate(candidates, 20m, allowShortfall: false);

        Assert.Single(result);
        Assert.Equal("STOCKED", result[0].Candidate.BatchNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Allocates_nothing_for_a_non_positive_quantity(decimal quantity)
    {
        var result = FefoAllocation.Allocate(new[] { Batch(1, "A", 100m) }, quantity, false);

        Assert.Empty(result);
    }

    [Fact]
    public void Handles_fractional_quantities()
    {
        // Seeds genuinely sell in fractions of a kilo, so the allocator must
        // not assume whole units anywhere.
        var candidates = new[] { Batch(1, "A", 2.5m), Batch(2, "B", 5m) };

        var result = FefoAllocation.Allocate(candidates, 4.25m, allowShortfall: false);

        Assert.Equal(2, result.Count);
        Assert.Equal(2.5m, result[0].Quantity);
        Assert.Equal(1.75m, result[1].Quantity);
    }

    [Fact]
    public void Empty_candidate_list_allocates_nothing_even_when_negative_is_allowed()
    {
        // With no batch at all there is nowhere to book the movement; the
        // caller falls back to a generic batch rather than inventing one here.
        var result = FefoAllocation.Allocate(Array.Empty<AllocationCandidate>(), 10m, true);

        Assert.Empty(result);
    }

    [Fact]
    public void Total_available_sums_the_candidates()
    {
        var candidates = new[] { Batch(1, "A", 10.5m), Batch(2, "B", 4.25m), Batch(3, "C", 0m) };

        Assert.Equal(14.75m, FefoAllocation.TotalAvailable(candidates));
    }
}
