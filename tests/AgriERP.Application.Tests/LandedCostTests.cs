using AgriERP.Application.Common.Services;

namespace AgriERP.Application.Tests;

/// <summary>
/// Landed-cost apportionment.
///
/// This arithmetic sets the cost of every batch and therefore the profit on
/// every sale. Getting it wrong does not throw - it quietly misstates margin
/// for as long as the batch survives, which is why it is worth pinning down
/// case by case.
/// </summary>
public class LandedCostTests
{
    [Fact]
    public void Divides_by_quantity_when_there_are_no_extra_charges()
    {
        var lines = new[] { new CostableLine(TaxableAmount: 40000m, Quantity: 100m, FreeQuantity: 0m) };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 0m);

        Assert.Equal(400m, rates[0]);
    }

    /// <summary>
    /// The worked example from the transaction suite: 100 @ 400 and 50 @ 420
    /// with 1,000 freight. Freight is split 40000:21000, so 655.74 and 344.26.
    /// </summary>
    [Fact]
    public void Spreads_charges_across_lines_in_proportion_to_value()
    {
        var lines = new[]
        {
            new CostableLine(40000m, 100m, 0m),
            new CostableLine(21000m, 50m, 0m),
        };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 1000m);

        Assert.Equal(406.5574m, rates[0]);
        Assert.Equal(426.8852m, rates[1]);
    }

    /// <summary>
    /// Free goods carry cost but no invoice value. Dividing by paid quantity
    /// alone would ignore that a "10 + 1 free" scheme is exactly what makes
    /// the deal profitable - and would overstate the cost of every unit.
    /// </summary>
    [Fact]
    public void Divides_by_total_quantity_including_free_goods()
    {
        var lines = new[] { new CostableLine(TaxableAmount: 4000m, Quantity: 10m, FreeQuantity: 1m) };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 0m);

        // 4000 over 11 units, not over 10.
        Assert.Equal(363.6364m, rates[0]);
    }

    [Fact]
    public void A_scheme_lowers_the_effective_cost_per_unit()
    {
        var withoutFree = LandedCost.Apportion(new[] { new CostableLine(1000m, 10m, 0m) }, 0m);
        var withFree = LandedCost.Apportion(new[] { new CostableLine(1000m, 10m, 2m) }, 0m);

        Assert.True(withFree[0] < withoutFree[0]);
    }

    [Fact]
    public void A_zero_quantity_line_gets_a_zero_rate_rather_than_dividing_by_zero()
    {
        var lines = new[] { new CostableLine(500m, 0m, 0m) };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 100m);

        Assert.Equal(0m, rates[0]);
    }

    [Fact]
    public void Zero_value_lines_do_not_break_the_apportionment()
    {
        // A fully-discounted line has no taxable value to attract a freight
        // share, but must still produce a rate rather than a division by zero.
        var lines = new[]
        {
            new CostableLine(0m, 10m, 0m),
            new CostableLine(1000m, 10m, 0m),
        };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 100m);

        Assert.Equal(0m, rates[0]);
        Assert.Equal(110m, rates[1]);   // takes the whole 100
    }

    [Fact]
    public void Charges_with_no_value_anywhere_are_simply_not_spread()
    {
        var lines = new[] { new CostableLine(0m, 5m, 0m) };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 500m);

        // Nothing to apportion against; the freight cannot be attributed and
        // is left out rather than dumped onto an arbitrary line.
        Assert.Equal(0m, rates[0]);
    }

    [Fact]
    public void Keeps_four_decimal_places()
    {
        // A rate rounded to paise loses real money once multiplied back out
        // over a few hundred units.
        var lines = new[] { new CostableLine(1000m, 3m, 0m) };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 0m);

        Assert.Equal(333.3333m, rates[0]);
    }

    [Fact]
    public void Returns_one_rate_per_line_in_the_same_order()
    {
        var lines = new[]
        {
            new CostableLine(100m, 1m, 0m),
            new CostableLine(200m, 1m, 0m),
            new CostableLine(300m, 1m, 0m),
        };

        var rates = LandedCost.Apportion(lines, chargesToSpread: 0m);

        Assert.Equal(3, rates.Count);
        Assert.Equal(100m, rates[0]);
        Assert.Equal(200m, rates[1]);
        Assert.Equal(300m, rates[2]);
    }

    [Fact]
    public void Handles_an_empty_line_list()
    {
        Assert.Empty(LandedCost.Apportion(Array.Empty<CostableLine>(), 500m));
    }
}
