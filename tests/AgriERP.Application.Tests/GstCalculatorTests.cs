using AgriERP.Application.Common.Services;

namespace AgriERP.Application.Tests;

/// <summary>
/// GST arithmetic. Pure - no database, no server, no clock.
///
/// These are the numbers that reach a printed tax invoice and a filed return,
/// so the cases below are the ones that actually go wrong in practice: half-way
/// rounding, the CGST/SGST split not summing to the total, and zero-rated
/// goods (seeds) being handed a tax line they should not have.
/// </summary>
public class GstCalculatorTests
{
    private readonly GstCalculator _gst = new();

    [Theory]
    [InlineData(1000, 18, 90, 90)]     // even split
    [InlineData(1500, 18, 135, 135)]   // the worked example on a real bill
    [InlineData(1000, 5, 25, 25)]      // fertilizer slab
    [InlineData(1000, 12, 60, 60)]     // sprayers and hand tools
    public void Intra_state_splits_tax_into_equal_cgst_and_sgst(
        decimal taxable, decimal rate, decimal expectedCgst, decimal expectedSgst)
    {
        var split = _gst.Split(taxable, rate, isInterState: false);

        Assert.Equal(expectedCgst, split.Cgst);
        Assert.Equal(expectedSgst, split.Sgst);
        Assert.Equal(0m, split.Igst);
    }

    [Fact]
    public void Inter_state_puts_the_whole_tax_in_igst()
    {
        var split = _gst.Split(1000m, 18m, isInterState: true);

        Assert.Equal(180m, split.Igst);
        Assert.Equal(0m, split.Cgst);
        Assert.Equal(0m, split.Sgst);
    }

    /// <summary>
    /// The reason CGST is rounded and SGST takes the remainder rather than
    /// both being rounded independently. On an odd total tax, rounding each
    /// half separately produces a sum one paisa away from the total - and that
    /// single paisa is what makes a GSTR-1 filing fail reconciliation.
    /// </summary>
    [Theory]
    [InlineData(105.50, 18)]
    [InlineData(333.33, 18)]
    [InlineData(1234.57, 5)]
    [InlineData(99.99, 12)]
    [InlineData(7777.77, 28)]
    public void Cgst_plus_sgst_always_equals_the_total_tax(decimal taxable, decimal rate)
    {
        var split = _gst.Split(taxable, rate, isInterState: false);
        var expectedTotal = _gst.Money(taxable * rate / 100m);

        Assert.Equal(expectedTotal, split.Cgst + split.Sgst);
        Assert.Equal(expectedTotal, split.Total);
    }

    [Fact]
    public void Zero_rated_goods_get_no_tax_at_all()
    {
        // Seeds for sowing are 0% - a tax line of 0.00 on the invoice would be
        // wrong, not merely harmless.
        var split = _gst.Split(5000m, 0m, isInterState: false);

        Assert.Equal(TaxSplit.Zero, split);
        Assert.Equal(0m, split.Total);
    }

    [Fact]
    public void Cess_is_charged_alongside_gst_not_instead_of_it()
    {
        var split = _gst.Split(1000m, 18m, isInterState: false, cessPercent: 2m);

        Assert.Equal(90m, split.Cgst);
        Assert.Equal(90m, split.Sgst);
        Assert.Equal(20m, split.Cess);
        Assert.Equal(200m, split.Total);
    }

    /// <summary>
    /// AwayFromZero, not .NET's banker's rounding. Banker's would turn 0.125
    /// into 0.12, which is not what a shopkeeper or a tax officer gets when
    /// they check the arithmetic by hand.
    /// </summary>
    [Theory]
    [InlineData(0.125, 0.13)]
    [InlineData(0.135, 0.14)]
    [InlineData(2.345, 2.35)]
    [InlineData(2.355, 2.36)]
    public void Money_rounds_half_away_from_zero_not_to_even(decimal input, decimal expected)
    {
        Assert.Equal(expected, _gst.Money(input));
    }

    [Theory]
    [InlineData(1234.60, 0.40)]   // rounds up to 1235
    [InlineData(1234.40, -0.40)]  // rounds down to 1234
    [InlineData(1234.00, 0.00)]   // already whole
    [InlineData(1234.50, 0.50)]   // half goes up
    public void Round_off_returns_the_adjustment_to_reach_a_whole_rupee(
        decimal grandTotal, decimal expectedAdjustment)
    {
        var adjustment = _gst.RoundOffAdjustment(grandTotal);

        Assert.Equal(expectedAdjustment, adjustment);
        // Whatever the adjustment, applying it must land on a whole rupee.
        Assert.Equal(Math.Round(grandTotal + adjustment), grandTotal + adjustment);
    }

    [Fact]
    public void Negative_taxable_amount_produces_negative_tax()
    {
        // Credit notes carry negative values through the same calculator; it
        // must not silently clamp them to zero and understate the reversal.
        var split = _gst.Split(-1000m, 18m, isInterState: false);

        Assert.Equal(-90m, split.Cgst);
        Assert.Equal(-90m, split.Sgst);
        Assert.Equal(-180m, split.Total);
    }
}
